using System.Text.Json;
using System.Text.Json.Nodes;
using Nocturne.Core.Models.V4;
using Nocturne.Infrastructure.Data.Entities.V4;
using Nocturne.Infrastructure.Data.Mappers.V4;

namespace Nocturne.Infrastructure.Data.Tests.Mappers.V4;

public class TherapySettingsMapperTests
{
    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(true)]
    [InlineData(false)]
    public void TrioFields_RoundTripAndReplaceExistingEntityValues(bool present)
    {
        var expected = JsonNode.Parse(present ? TrioFields : "{}")!;
        var model = JsonSerializer.Deserialize<TherapySettings>(expected.ToJsonString(), JsonSerializerOptions.Web)!;
        var opposite = JsonSerializer.Deserialize<TherapySettings>(present ? "{}" : TrioFields, JsonSerializerOptions.Web)!;
        var entity = TherapySettingsMapper.ToEntity(model);
        var updated = TherapySettingsMapper.ToEntity(opposite);

        TherapySettingsMapper.UpdateEntity(updated, model);

        foreach (var stored in new[] { entity, updated })
        {
            var actual = JsonSerializer.SerializeToNode(TherapySettingsMapper.ToDomainModel(stored), JsonSerializerOptions.Web)!;
            foreach (var field in JsonNode.Parse(TrioFields)!.AsObject())
                JsonNode.DeepEquals(actual[field.Key], expected[field.Key]).Should().BeTrue(field.Key);
        }
    }

    private const string TrioFields = """
        {"deviceToken":"trio-token","bundleIdentifier":"org.triodocs.Trio","teamID":"TRIOTEAM42",
         "expirationDate":"2027-01-02T03:04:05.123456+05:30",
         "overridePresets":[{"name":"Exercise","duration":45.5,"percentage":80,"target":110},{"name":"Name only"}]}
        """;

    [Fact]
    [Trait("Category", "Unit")]
    public void ToDomainModel_MalformedLoopSettingsJson_YieldsNoLoopSettings()
    {
        var entity = new TherapySettingsEntity
        {
            Id = Guid.CreateVersion7(),
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(1700000000000).UtcDateTime,
            LoopSettingsJson = """{"dia":""",
        };

        var model = TherapySettingsMapper.ToDomainModel(entity);

        model.LoopSettings.Should().BeNull(
            "one unparseable jsonb row must not fail the read of every record around it");
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(true)]
    [InlineData(false)]
    [InlineData(null)]
    public void ApnsEnvironment_RoundTripsThroughTheEntity(bool? isApnsProduction)
    {
        var model = new TherapySettings { IsAPNSProduction = isApnsProduction };

        var entity = TherapySettingsMapper.ToEntity(model);
        TherapySettingsMapper.ToDomainModel(entity).IsAPNSProduction.Should().Be(isApnsProduction);

        var updated = new TherapySettingsEntity { IsApnsProduction = !isApnsProduction };
        TherapySettingsMapper.UpdateEntity(updated, model);
        updated.IsApnsProduction.Should().Be(isApnsProduction);
    }
}
