using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Nocturne.API.Configuration;
using Nocturne.Core.Models;
using Xunit;

namespace Nocturne.API.Tests.Configuration;

/// <summary><see cref="NightscoutJsonOptions"/>.</summary>
public class NightscoutJsonOptionsTests
{
    private static JsonObject Serialize(Treatment treatment) =>
        JsonNode.Parse(JsonSerializer.Serialize(treatment, NightscoutJsonOptions.Create()))!.AsObject();

    [Fact]
    public void An_indefinite_override_without_a_duration_is_served_without_one()
    {
        var served = Serialize(new Treatment { EventType = "Temporary Override", DurationType = "indefinite" });

        served.ContainsKey("duration").Should().BeFalse();
        served["durationType"]!.GetValue<string>().Should().Be("indefinite");
    }

    /// <summary>AAPS reads a zero-duration Temporary Target as a cancel and a zero-duration Profile Switch as permanent.</summary>
    [Theory]
    [InlineData("Temporary Target")]
    [InlineData("Profile Switch")]
    [InlineData("Note")]
    public void A_zero_duration_on_a_treatment_that_is_not_indefinite_is_served(string eventType)
    {
        var served = Serialize(new Treatment { EventType = eventType, Duration = 0 });

        served["duration"]!.GetValue<double>().Should().Be(0);
    }

    [Theory]
    [InlineData(false, false, "false", "false")]
    [InlineData(true, true, "true", "true")]
    [InlineData(false, true, "false", "true")]
    public void Food_flags_are_served_as_strings_and_always_present(
        bool hidden, bool hideAfterUse, string expectedHidden, string expectedHideAfterUse)
    {
        var served = JsonNode.Parse(
            JsonSerializer.Serialize(
                new Food { Name = "Snack", Hidden = hidden, HideAfterUse = hideAfterUse },
                NightscoutJsonOptions.Create()))!.AsObject();

        served["hidden"]!.GetValue<string>().Should().Be(expectedHidden);
        served["hideafteruse"]!.GetValue<string>().Should().Be(expectedHideAfterUse);
    }

    [Fact]
    public void Food_flags_read_back_from_the_strings_they_are_served_as()
    {
        var food = JsonSerializer.Deserialize<Food>("""{"hidden":"true","hideafteruse":"false"}""", NightscoutJsonOptions.Create())!;

        food.Hidden.Should().BeTrue();
        food.HideAfterUse.Should().BeFalse();
    }

    [Fact]
    public void A_finite_duration_on_an_indefinite_override_is_served()
    {
        var served = Serialize(new Treatment
        {
            EventType = "Temporary Override", DurationType = "indefinite", Duration = 25,
        });

        served["duration"]!.GetValue<double>().Should().Be(25);
    }

    [Fact]
    public void Profile_zero_values_are_served_while_unset_nullable_fields_stay_absent()
    {
        var profile = new ProfileData
        {
            Dia = 0,
            CarbsHr = 0,
            Delay = 0,
            Basal = [new() { Value = 0 }],
            CarbRatio = [new() { Value = 0 }],
            Sens = [new() { Value = 0 }],
            TargetLow = [new() { Value = 0 }],
            TargetHigh = [new() { Value = 0 }],
        };

        var served = JsonNode.Parse(JsonSerializer.Serialize(profile, NightscoutJsonOptions.Create()))!.AsObject();

        using var assertions = new FluentAssertions.Execution.AssertionScope();
        foreach (var field in new[] { "dia", "carbs_hr", "delay" })
        {
            served.ContainsKey(field).Should().BeTrue($"{field} is required even at zero");
            served[field]?.GetValue<double>().Should().Be(0);
        }
        foreach (var schedule in new[] { "basal", "carbratio", "sens", "target_low", "target_high" })
        {
            var entry = served[schedule]!.AsArray().Single()!.AsObject();
            entry.ContainsKey("value").Should().BeTrue($"{schedule} entries require value even at zero");
            entry["value"]?.GetValue<double>().Should().Be(0);
        }
        foreach (var field in new[]
        {
            "timezone", "units", "perGIvalues", "carbs_hr_high", "carbs_hr_medium", "carbs_hr_low",
            "delay_high", "delay_medium", "delay_low",
        })
        {
            served.ContainsKey(field).Should().BeFalse($"unset nullable {field} stays absent");
        }
    }
}
