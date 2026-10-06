using System.Text.Json;
using KeyLoad.Comparisons;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class VectorProfileContractTests
{
    [Test]
    public async Task AllProfilesRoundTripWithoutAllowingModifiedSettingsOrExtraFields()
    {
        await Assert.That(VectorComparisonProfile.AllIds.Count).IsEqualTo(24);
        foreach (var id in VectorComparisonProfile.AllIds)
        {
            var profile = VectorComparisonProfile.Parse(id);
            var json = JsonSerializer.Serialize(profile, ReportWriter.JsonOptions);
            var actual = JsonSerializer.Deserialize<VectorComparisonProfile>(json, ReportWriter.JsonOptions);
            await Assert.That(actual).IsEqualTo(profile);
            var extra = json[..^1] + ",\"unexpected\":1}";
            await Assert.That(() => JsonSerializer.Deserialize<VectorComparisonProfile>(extra, ReportWriter.JsonOptions))
                .Throws<JsonException>();
            var altered = json.Replace("128", "127", StringComparison.Ordinal);
            await Assert.That(() => JsonSerializer.Deserialize<VectorComparisonProfile>(altered, ReportWriter.JsonOptions))
                .Throws<JsonException>();
            await Assert.That(profile.RecordCount is 100_000 or 1_000_000).IsTrue();
        }
    }

    [Test]
    [Arguments("vector-5m-exact-plain-c16")]
    [Arguments("Vector-100k-exact-plain-c16")]
    [Arguments("vector-100k-exact-plain-c17")]
    [Arguments("vector-100k-hnsw-plain-c16-")]
    [Arguments("vector-100k-exact-unfiltered-c16")]
    public async Task UnknownScaleCasingConcurrencyAndModesAreRejected(string id)
        => await Assert.That(() => VectorComparisonProfile.Parse(id)).Throws<ArgumentOutOfRangeException>();
}
