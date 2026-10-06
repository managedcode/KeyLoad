using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Comparisons;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class ScaledComparisonProfileTests
{
    [Test]
    public async Task OnlyTwoExactProfilesCarryTheFrozenS1Settings()
    {
        var profiles = new[]
        {
            ScaledComparisonProfileParser.Parse("scaled-100k-c16"),
            ScaledComparisonProfileParser.Parse("scaled-1m-c16")
        };
        await Assert.That(profiles.Select(profile => profile.Documents)).IsEquivalentTo(new[] { 100_000, 1_000_000 });
        foreach (var profile in profiles)
        {
            await Assert.That(profile.Operations).IsEqualTo(100_000);
            await Assert.That(profile.Warmup).IsEqualTo(256);
            await Assert.That(profile.Repetitions).IsEqualTo(1);
            await Assert.That(profile.Concurrency).IsEqualTo(16);
            await Assert.That(profile.PayloadBytes).IsEqualTo(1_024);
            await Assert.That(profile.Seed).IsEqualTo(1_729);
        }
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ScaledComparisonProfileParser.Parse("scaled-5000000-c16"));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ScaledComparisonProfileParser.Parse("scaled-5m-c16"));
    }
    [Test]
    public async Task ProfileJsonRoundTripIsClosedOverTheParsedIdentityAndSettings()
    {
        var profile = ScaledComparisonProfileParser.Parse("scaled-1m-c16");
        var json = JsonSerializer.Serialize(profile);
        var restored = JsonSerializer.Deserialize<ScaledComparisonProfile>(json)!;
        await Assert.That(restored).IsEqualTo(profile);
        using var document = JsonDocument.Parse(json);
        await Assert.That(document.RootElement.EnumerateObject().Count()).IsEqualTo(14);
        await Assert.That(document.RootElement.GetProperty(IsolatedPlanFields.Id).GetString()).IsEqualTo(profile.Id);
        await Assert.That(document.RootElement.GetProperty(IsolatedPlanFields.Documents).GetInt32()).IsEqualTo(1_000_000);
        var altered = json.Replace("1000000", "5000000", StringComparison.Ordinal);
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<ScaledComparisonProfile>(altered));
        var extra = json[..^1] + ",\"Other\":1}";
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<ScaledComparisonProfile>(extra));
    }

    [Test]
    public void ComparisonReportRequiresExactlyOneControlOrScaledConfiguration()
    {
        var profile = ScaledComparisonProfileParser.Parse("scaled-100k-c16");
        Assert.ThrowsExactly<InvalidOperationException>(() => Report(null, null).ValidateConfiguration());
        Assert.ThrowsExactly<InvalidOperationException>(() => Report(new(), profile).ValidateConfiguration());
        Report(new(), null).ValidateConfiguration();
        Report(null, profile).ValidateConfiguration();
    }

    private static ComparisonReport Report(ComparisonOptions? options, ScaledComparisonProfile? profile)
        => new(3, Guid.NewGuid(), DateTimeOffset.UnixEpoch, options, "digest", "load", "linux", "x64", 1,
            ".NET 10", "native", null, ImmutableArray<TargetProfile>.Empty, ImmutableArray<ComparisonCase>.Empty)
        { ScaledProfile = profile };

}
