namespace KeyLoad.ClusterTests.DatabaseComposition;

[NotInParallel]
internal sealed class FunctionalDatabaseGateTests
{
    private const int ThreeNodeCluster = 3;

    [Test]
    public Task SingleInstanceRunsEveryModelThroughSdkMcpAndSqlWithPersistedUsers()
        => FunctionalGateScenario.RunAsync(1, TestContext.Current!.Execution.CancellationToken);

    [Test]
    public Task ThreeNodeClusterRunsSameEveryModelScenarioThroughSdkMcpAndSql()
        => FunctionalGateScenario.RunAsync(ThreeNodeCluster, TestContext.Current!.Execution.CancellationToken);
}
