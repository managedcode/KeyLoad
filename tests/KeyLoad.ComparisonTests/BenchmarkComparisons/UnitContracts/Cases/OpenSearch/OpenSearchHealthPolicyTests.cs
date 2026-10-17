using System.Globalization;
using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class OpenSearchHealthPolicyTests
{
    [Test]
    public async Task OpenSearchHealthUriUsesConfiguredInvariantWholeSecondsAndRecordsPolicy()
    {
        var options = UnitBenchmarkOptions.Native();
        options.Value.OpenSearchHealthWaitTimeoutSeconds = new NativeComparisonExecutionOptions().OpenSearchHealthWaitTimeoutSeconds;
        await Assert.That(OpenSearchClusterEvidence.BuildHealthPath("native-index", options))
            .IsEqualTo("/_cluster/health/native-index?wait_for_status=green&wait_for_active_shards=all&timeout=60s&level=shards");

        options.Value.OpenSearchHealthWaitTimeoutSeconds = 7;
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            await Assert.That(OpenSearchClusterEvidence.BuildHealthPath("native-index", options))
                .IsEqualTo("/_cluster/health/native-index?wait_for_status=green&wait_for_active_shards=all&timeout=7s&level=shards");
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }

        var evidence = new Dictionary<string, string>();
        options.Value.RecordEvidence(evidence);
        await Assert.That(evidence[nameof(options.Value.OpenSearchHealthWaitTimeoutSeconds)]).IsEqualTo("7");
    }

    [Test]
    [Arguments(0)]
    [Arguments(61)]
    public async Task InvalidOpenSearchHealthTimeoutFailsBeforeObservation(int timeoutSeconds)
    {
        var options = UnitBenchmarkOptions.Native();
        options.Value.OpenSearchHealthWaitTimeoutSeconds = timeoutSeconds;
        using var client = new HttpClient();
        await Assert.ThrowsExactlyAsync<OptionsValidationException>(() => OpenSearchClusterEvidence.ObserveAsync(
            client, "native-index", 1, ComparisonTopology.Standalone, options, CancellationToken.None));
    }
}
