using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Nocturne.API.Services.Migration;
using Nocturne.Core.Models;

namespace Nocturne.API.Tests.Migration;

public class ProfileTrioFieldsMigrationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ApiImport_CarriesTrioFieldsToDecomposition(bool present)
    {
        const string fields = """
            {"deviceToken":"trio-token","bundleIdentifier":"org.triodocs.Trio","teamID":"TRIOTEAM42",
             "expirationDate":"2027-01-02T03:04:05.123456+05:30",
             "overridePresets":[{"name":"Exercise","duration":45.5,"percentage":80,"target":110},{"name":"Name only"}]}
            """;
        var expected = JsonNode.Parse(present ? fields : "{}")!.AsObject();
        var document = (JsonObject)expected.DeepClone();
        document["_id"] = "profile1";
        document["defaultProfile"] = "Default";
        document["store"] = new JsonObject();
        using var source = new ProfileSource(document.ToJsonString());
        Profile? imported = null;
        await using var provider = MigrationJobHarness.BuildProvider(source, profileOutcome: profile =>
        {
            imported = profile;
            return new();
        });

        var status = await MigrationJobHarness.RunAsync(provider, "profile");

        status.State.Should().Be(MigrationJobState.Completed, status.ErrorMessage);
        imported.Should().NotBeNull();
        var actual = JsonSerializer.SerializeToNode(imported)!;
        foreach (var field in JsonNode.Parse(fields)!.AsObject())
        {
            actual.AsObject().ContainsKey(field.Key).Should().Be(present, field.Key);
            JsonNode.DeepEquals(actual[field.Key], expected[field.Key]).Should().BeTrue(field.Key);
        }
    }

    private sealed class ProfileSource(string document) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.RequestUri!.AbsolutePath switch
            {
                "/api/v1/status" => """{"status":"ok"}""",
                "/api/v1/profile.json" => "[" + document + "]",
                _ => "[]",
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
