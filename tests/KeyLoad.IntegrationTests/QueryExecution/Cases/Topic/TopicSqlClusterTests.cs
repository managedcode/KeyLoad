using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.QueryExecution;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class TopicSqlClusterTests(ClusterFixture fixture)
{
    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task ActualTopicSqlAstCurrentPolicyOriginalReceiptAndUntouchedQueueSurviveTwoColdRestarts(int route)
    {
        using var deadline = McpCallerDeadline.Create();
        var state = await TopicSqlClusterSeed.CreateAsync(fixture, deadline.Token);
        await TopicSqlClusterPhase.AuthorityAsync(fixture, state, route, deadline.Token);
        await TopicSqlClusterPhase.HealthyAsync(fixture, state, route, deadline.Token);
        await ModelSqlClusterCold.RunAsync(fixture, deadline.Token);
        await TopicSqlClusterPhase.HealthyAsync(fixture, state, route, deadline.Token);
        await ModelSqlClusterCold.RunAsync(fixture, deadline.Token);
        await TopicSqlClusterPhase.HealthyAsync(fixture, state, route, deadline.Token);
    }
}
