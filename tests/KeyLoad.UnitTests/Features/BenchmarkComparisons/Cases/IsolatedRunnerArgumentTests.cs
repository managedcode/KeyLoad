using KeyLoad.Comparisons;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class IsolatedRunnerArgumentTests
{
    [Test]
    public async Task AcIso002UndefinedSelectedScenarioFailsBeforeTargetInitialization()
    {
        var runner = new ComparisonRunner(new());
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => runner.RunAsync([], null,
            CancellationToken.None, scenario: (Scenario)100));
    }

    [Test]
    public async Task AcIso002SelectedScenarioRejectsMultipleOrMissingTargetBeforeAllocation()
    {
        var runner = new ComparisonRunner(new());
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => runner.RunAsync([], null,
            CancellationToken.None, scenario: Scenario.PointRead));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => runner.RunAsync(new IComparisonTarget[2], null,
            CancellationToken.None, scenario: Scenario.PointRead));
    }
}
