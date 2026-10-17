namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

[NotInParallel]
internal sealed class FunctionalDatabaseGateTests
{
    private const int SingleInstance = 1;
    private const int ThreeNodeCluster = 3;

    [Test]
    public Task SingleInstanceRunsEveryModelThroughSdkMcpAndSqlWithPersistedUsers()
        => FunctionalGateScenario.RunAsync(SingleInstance, TestContext.Current!.Execution.CancellationToken);

    [Test]
    public Task ThreeNodeClusterRunsSameEveryModelScenarioThroughSdkMcpAndSql()
        => FunctionalGateScenario.RunAsync(ThreeNodeCluster, TestContext.Current!.Execution.CancellationToken);
}
