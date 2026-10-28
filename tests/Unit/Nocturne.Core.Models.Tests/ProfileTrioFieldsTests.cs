using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Xunit;

namespace Nocturne.Core.Models.Tests;

public class ProfileTrioFieldsTests
{
    [Fact]
    public void Upload_PrimitivePushTargetValues_UsesFlexibleStrings()
    {
        var profile = JsonSerializer.Deserialize<Profile>("""
            {"deviceToken":123,"bundleIdentifier":true,"teamID":456,"expirationDate":45.5}
            """)!;

        profile.DeviceToken.Should().Be("123");
        profile.BundleIdentifier.Should().Be("true");
        profile.TeamID.Should().Be("456");
        profile.ExpirationDate.Should().Be("45.5");
    }

    [Theory]
    [InlineData("""{"deviceToken":"trio-token","bundleIdentifier":"org.triodocs.Trio","teamID":"TRIOTEAM42","expirationDate":"2027-01-02T03:04:05.123456+05:30","overridePresets":[{"name":"Exercise","duration":45.5,"percentage":80,"target":110},{"name":"Name only"}]}""")]
    [InlineData("{}")]
    [InlineData("""{"overridePresets":[{"name":"Exercise","duration":"45","extra":true},1]}""")]
    [InlineData("""{"overridePresets":{"custom":[1,true]}}""")]
    [InlineData("""{"overridePresets":"custom"}""")]
    [InlineData("""{"overridePresets":42}""")]
    [InlineData("""{"overridePresets":false}""")]
    public void Upload_PreservesTrioFieldsAndTheirAbsence(string json)
    {
        var expected = JsonNode.Parse(json)!;
        var profile = JsonSerializer.Deserialize<Profile>(json)!;

        var actual = JsonSerializer.SerializeToNode(profile)!;

        foreach (var key in new[] { "deviceToken", "bundleIdentifier", "teamID", "expirationDate", "overridePresets" })
        {
            actual.AsObject().ContainsKey(key).Should().Be(expected.AsObject().ContainsKey(key), key);
            JsonNode.DeepEquals(actual[key], expected[key]).Should().BeTrue(key);
        }
        actual.AsObject().ContainsKey("teamId").Should().BeFalse();
    }
}
