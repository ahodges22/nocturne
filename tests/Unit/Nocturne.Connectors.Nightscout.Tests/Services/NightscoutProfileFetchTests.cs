using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nocturne.Connectors.Core.Interfaces;
using Nocturne.Connectors.Core.Models;
using Nocturne.Connectors.Nightscout.Configurations;
using Nocturne.Connectors.Nightscout.Services;
using Nocturne.Connectors.Nightscout.Tests.TestSupport;
using Nocturne.Core.Contracts.V4;
using Nocturne.Core.Models;
using Xunit;

namespace Nocturne.Connectors.Nightscout.Tests.Services;

public class NightscoutProfileFetchTests
{
    [Fact]
    public async Task SyncProfiles_UnusualTrioFields_KeepsBothProfiles()
    {
        const string response = """
            [{"_id":"trio","deviceToken":123,"overridePresets":[1]},
             {"_id":"clinical","defaultProfile":"Default","store":{"Default":{
              "dia":3,"basal":[{"time":"00:00","value":1}],
              "carbratio":[{"time":"00:00","value":10}],"sens":[{"time":"00:00","value":50}],
              "target_low":[{"time":"00:00","value":80}],"target_high":[{"time":"00:00","value":120}]}}}]
            """;

        var profiles = await FetchProfilesAsync(response);

        profiles.Select(p => p.Id).Should().Equal("trio", "clinical");
        var trio = JsonSerializer.SerializeToElement(profiles[0]);
        trio.GetProperty("deviceToken").GetString().Should().Be("123");
        trio.GetProperty("overridePresets").GetRawText().Should().Be("[1]");
        profiles[1].Store["Default"].Basal.Should().ContainSingle().Which.Value.Should().Be(1);
    }

    private static async Task<Profile[]> FetchProfilesAsync(string response)
    {
        using var handler = new RecordingHttpMessageHandler
        {
            Respond = uri => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(uri.AbsolutePath == "/api/v1/profile.json" ? response : "[]"),
            },
        };
        using var client = new HttpClient(handler);
        Profile[] profiles = [];
        var metadata = new Mock<IMetadataPublisher>();
        metadata.Setup(m => m.PublishProfilesAsync(It.IsAny<IEnumerable<Profile>>(), It.IsAny<string>(),
                It.IsAny<WriteOrigin>(), It.IsAny<CancellationToken>()))
            .Callback((IEnumerable<Profile> batch, string _, WriteOrigin _, CancellationToken _) => profiles = batch.ToArray())
            .ReturnsAsync(true);
        var publisher = new Mock<IConnectorPublisher>();
        publisher.Setup(p => p.IsAvailable).Returns(true);
        publisher.Setup(p => p.Metadata).Returns(metadata.Object);
        var registration = new Mock<IConnectorRegistration<NightscoutConnectorConfiguration>>();
        registration.Setup(r => r.Defaults).Returns(new NightscoutConnectorConfiguration());
        var service = new NightscoutConnectorService(client,
            Mock.Of<IConnectorServerResolver<NightscoutConnectorConfiguration>>(),
            NullLogger<NightscoutConnectorService>.Instance,
            Mock.Of<IRetryDelayStrategy>(), Mock.Of<IRateLimitingStrategy>(),
            registration.Object, publisher.Object);

        await service.SyncDataAsync(new SyncRequest { DataTypes = [SyncDataType.Profiles] },
            new NightscoutConnectorConfiguration { Url = "https://ns.example", ApiSecret = "secret", MaxRetryAttempts = 0 },
            CancellationToken.None);
        return profiles;
    }
}
