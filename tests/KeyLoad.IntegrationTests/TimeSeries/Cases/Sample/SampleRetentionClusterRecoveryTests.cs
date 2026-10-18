using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

/// <summary>AC-SERIES-013..016 exercise retention through real Aspire-owned CLUSTER nodes and callers.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class SampleRetentionClusterRecoveryTests(ClusterFixture fixture)
{
    [Test]
    public Task AcSeries013Through016PartialPurgeReplaysHidesAndSurvivesFollowerRestart()
        => SampleRetentionClusterRestartWorkflow.RunAsync(fixture, TestContext.Current!.Execution.CancellationToken);
}
