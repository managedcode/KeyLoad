using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.TimeSeries;

/// <summary>AC-SERIES-013..016 exercise retention through real Aspire-owned Orleans nodes and callers.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class SampleRetentionClusterRecoveryTests(ClusterFixture fixture)
{
    [Test]
    public Task AcSeries013Through016PartialPurgeReplaysHidesAndSurvivesSiloRestart()
        => SampleRetentionClusterRestartWorkflow.RunAsync(fixture, TestContext.Current!.Execution.CancellationToken);
}
