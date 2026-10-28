using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using Nocturne.API.Services.Migration;
using Nocturne.API.Tests.Integration.Infrastructure;
using Nocturne.Core.Contracts.Multitenancy;
using Nocturne.Core.Models;
using Testcontainers.MongoDb;
using Xunit;
using Xunit.Abstractions;

namespace Nocturne.API.Tests.Integration;

[Trait("Category", "Integration")]
public class ProfileTrioFieldsIntegrationTests(ApiIntegrationTestFixture fixture, ITestOutputHelper output)
    : ApiIntegrationTestBase(fixture, output)
{
    private const string Id = "5f8d0c1e8a7b4c3d9e5f0201";
    private const string TrioFields = """
        {"deviceToken":"trio-token","bundleIdentifier":"org.triodocs.Trio","teamID":"TRIOTEAM42",
         "expirationDate":"2027-01-02T03:04:05.123456+05:30",
         "overridePresets":[{"name":"Exercise","duration":45.5,"percentage":80,"target":110},{"name":"Name only"}]}
        """;

    private static JsonObject Document(bool present, string id = Id)
    {
        var document = JsonNode.Parse(present ? TrioFields : "{}")!.AsObject();
        document["_id"] = id;
        document["defaultProfile"] = "Default";
        document["startDate"] = "2026-10-01T00:00:00.000Z";
        document["store"] = JsonNode.Parse("""
            {"Default":{"dia":3,"timezone":"UTC","units":"mg/dl",
             "basal":[{"time":"00:00","value":1}],"carbratio":[{"time":"00:00","value":10}],
             "sens":[{"time":"00:00","value":50}],"target_low":[{"time":"00:00","value":80}],
             "target_high":[{"time":"00:00","value":120}]}}
            """);
        return document;
    }

    [Theory]
    [InlineData("v1-post", true)]
    [InlineData("v1-put", true)]
    [InlineData("v3-post", true)]
    [InlineData("v3-put", true)]
    [InlineData("v1-post", false)]
    public async Task Upload_PreservesTrioFieldsOnEveryLegacyRead(string ingest, bool present)
    {
        using var client = CreateAuthenticatedClient();
        var document = Document(present);
        if (ingest.EndsWith("put", StringComparison.Ordinal))
            (await client.PostAsJsonAsync("/api/v1/profile.json", Document(false))).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = ingest switch
        {
            "v1-post" => await client.PostAsJsonAsync("/api/v1/profile.json", document),
            "v1-put" => await client.PutAsJsonAsync("/api/v1/profile.json", document),
            "v3-post" => await client.PostAsJsonAsync("/api/v3/profile", document),
            "v3-put" => await client.PutAsJsonAsync($"/api/v3/profile/{Id}", document),
            _ => throw new ArgumentOutOfRangeException(nameof(ingest)),
        };
        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());

        await AssertLegacyReadsAsync(client, present);
    }

    [Theory]
    [InlineData("v1-post")]
    [InlineData("api-migration")]
    public async Task Import_UnusualPresets_ReadBackExactlyAsSent(string ingest)
    {
        var document = Document(true);
        document["overridePresets"] = JsonNode.Parse("""[{"name":"Exercise","duration":"45","extra":{"enabled":true}},1]""");
        using var client = CreateAuthenticatedClient();
        if (ingest == "api-migration")
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            await using var source = builder.Build();
            source.MapGet("/api/v1/status", () => Results.Json(new { status = "ok" }));
            source.MapGet("/api/v1/profile.json", () => Results.Json(new[] { document }));
            await source.StartAsync();
            await RunMigrationAsync(new StartMigrationRequest
            {
                Mode = MigrationMode.Api,
                NightscoutUrl = source.Urls.Single(),
                Collections = ["profile"],
            }, 1);
        }
        else
        {
            var response = await client.PostAsJsonAsync("/api/v1/profile.json", document);
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        }

        await AssertLegacyReadsAsync(client, true, document.ToJsonString());
        AssertStore((await ReadAsync(client, $"/api/v1/profile/{Id}")).EnumerateArray().Single(), document);
    }

    [Fact]
    public async Task MongoImport_UnusualPresets_PreservesStoreAndNormalizesBson()
    {
        var expected = Document(true);
        expected["store"]!["Weekend"] = expected["store"]!["Default"]!.DeepClone();
        expected["overridePresets"] = JsonNode.Parse("""
            [{"name":"Exercise","duration":5.5,"percentage":"45","target":null,
              "date":"2026-10-01T00:00:00.000Z"},{"name":"String duration","duration":"45"},1]
            """);
        var document = BsonDocument.Parse(expected.ToJsonString());
        var preset = document["overridePresets"][0].AsBsonDocument;
        preset["duration"] = new BsonDecimal128(5.5m);
        preset["target"] = new BsonDouble(double.NaN);
        preset["date"] = new BsonDateTime(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));

        await ImportMongoAsync(document);

        using var client = CreateAuthenticatedClient();
        var actual = (await ReadAsync(client, $"/api/v1/profile/{Id}")).EnumerateArray().Single();
        AssertTrioFields(actual, true, expected.ToJsonString());
        AssertStore(actual, expected);
    }

    [Fact]
    public async Task MongoImport_PreservesTrioFieldsAndLeavesAbsentFieldsAbsent()
    {
        const string absentId = "5f8d0c1e8a7b4c3d9e5f0202";
        var documents = new[] { Document(true), Document(false, absentId) }
            .Select(d => BsonDocument.Parse(d.ToJsonString())).ToArray();
        await ImportMongoAsync(documents);

        using var client = CreateAuthenticatedClient();
        AssertTrioFields((await ReadAsync(client, $"/api/v1/profile/{Id}")).EnumerateArray().Single(), true);
        AssertTrioFields((await ReadAsync(client, $"/api/v1/profile/{absentId}")).EnumerateArray().Single(), false);
    }

    private async Task ImportMongoAsync(params BsonDocument[] documents)
    {
        await using var mongo = new MongoDbBuilder("mongo:7").Build();
        await mongo.StartAsync();
        var database = new MongoClient(mongo.GetConnectionString()).GetDatabase("trio_profiles");
        foreach (var document in documents)
            document["_id"] = ObjectId.Parse(document["_id"].AsString);
        await database.GetCollection<BsonDocument>("profile").InsertManyAsync(documents);
        await RunMigrationAsync(new StartMigrationRequest
        {
            Mode = MigrationMode.MongoDb,
            MongoConnectionString = mongo.GetConnectionString(),
            MongoDatabaseName = "trio_profiles",
            Collections = ["profile"],
        }, documents.Length);
    }

    private async Task RunMigrationAsync(StartMigrationRequest request, int count)
    {
        var service = Fixture.Services.GetRequiredService<IMigrationJobService>();
        var job = await service.StartMigrationAsync(request,
            new TenantContext(Fixture.TenantId, ApiIntegrationTestFixture.TenantSlug, "Integration", true, false));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        MigrationJobStatus status;
        do
        {
            await Task.Delay(50, timeout.Token);
            status = await service.GetStatusAsync(Fixture.TenantId, job.Id);
        } while (status.State.IsLive());
        status.State.Should().Be(MigrationJobState.Completed, status.ErrorMessage);
        status.CollectionProgress["profile"].DocumentsMigrated.Should().Be(count);
        status.CollectionProgress["profile"].DocumentsFailed.Should().Be(0);
    }

    private static void AssertStore(JsonElement actual, JsonObject expected)
    {
        var actualStore = actual.Deserialize<Profile>()!.Store;
        var expectedStore = expected.Deserialize<Profile>()!.Store;
        actualStore.Should().BeEquivalentTo(expectedStore,
            options => options.Excluding(member => member.Name == nameof(TimeValue.TimeAsSeconds)));
    }

    private static async Task AssertLegacyReadsAsync(HttpClient client, bool present, string fields = TrioFields)
    {
        foreach (var path in new[]
        {
            "/api/v1/profile.json", "/api/v1/profiles.json", $"/api/v1/profile/{Id}",
            "/api/v3/profile", "/api/v3/profile/history/0",
        })
            AssertTrioFields((await ReadAsync(client, path)).EnumerateArray().Single(), present, fields);
        AssertTrioFields(await ReadAsync(client, "/api/v1/profile/current"), present, fields);
        var identifier = (await ReadAsync(client, "/api/v3/profile")).EnumerateArray().Single().GetProperty("identifier").GetString();
        AssertTrioFields(await ReadAsync(client, $"/api/v3/profile/{identifier}"), present, fields);
        AssertTrioFields((await ReadAsync(client, "/api/v2/ddata")).GetProperty("profiles").EnumerateArray().Single(), present, fields);
    }

    private static async Task<JsonElement> ReadAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        response.StatusCode.Should().Be(HttpStatusCode.OK, path);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.ValueKind == JsonValueKind.Object && body.TryGetProperty("result", out var result) ? result : body;
    }

    private static void AssertTrioFields(JsonElement profile, bool present, string fields = TrioFields)
    {
        foreach (var field in JsonNode.Parse(TrioFields)!.AsObject())
        {
            profile.TryGetProperty(field.Key, out var value).Should().Be(present, field.Key);
            if (present)
                JsonNode.DeepEquals(JsonNode.Parse(value.GetRawText()), JsonNode.Parse(fields)![field.Key]).Should().BeTrue(field.Key);
        }
        profile.TryGetProperty("teamId", out _).Should().BeFalse();
    }
}
