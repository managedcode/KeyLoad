using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

/// <summary>AC-SERIES-013..016 exercise retention through real Aspire-owned RF3 nodes and callers.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class SampleRetentionRf3RecoveryTests(ClusterFixture fixture)
{
    [Test]
    public Task AcSeries013Through016PartialPurgeReplaysHidesAndSurvivesFollowerRestart()
        => SampleRetentionRf3RestartWorkflow.RunAsync(fixture, TestContext.Current!.Execution.CancellationToken);
}
