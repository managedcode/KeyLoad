using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class IsolatedSetupFailureTests
{
    private const string InvalidRedisConfiguration = "localhost,ssl=invalid";
    private const string SetupPrefix = "setup:";

    [Test]
    public async Task AcIso006UnsupportedCapabilityCannotHideNativeInitializationFailure()
    {
        await using var target = new RedisTarget(InvalidRedisConfiguration, Guid.NewGuid().ToString(), string.Empty, UnitBenchmarkOptions.Lifecycle(), UnitBenchmarkOptions.Native(), UnitBenchmarkOptions.Diagnostics());
        var runner = new ComparisonRunner(Microsoft.Extensions.Options.Options.Create(ComparisonHarnessInputs.Small), UnitBenchmarkOptions.Native());
        var report = await runner.RunAsync([target], null, CancellationToken.None, scenario: Scenario.VectorExact);
        await Assert.That(report.Cases.Length).IsEqualTo(ComparisonHarnessInputs.Small.Repetitions);
        foreach (var result in report.Cases)
        {
            await Assert.That(result.Status).IsEqualTo(ComparisonStatuses.Failed);
            await Assert.That(result.Detail).StartsWith(SetupPrefix);
            await Assert.That(result.Measurement).IsNull();
            await Assert.That(result.Samples.Length).IsEqualTo(0);
        }
    }
}
