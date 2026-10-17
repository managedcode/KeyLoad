using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class TopicSqlRf3Tests(ClusterFixture fixture)
{
    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task ActualTopicSqlAstCurrentPolicyOriginalReceiptAndUntouchedQueueSurviveTwoColdRestarts(int route)
    {
        using var deadline = McpCallerDeadline.Create();
        var state = await TopicSqlRf3Seed.CreateAsync(fixture, deadline.Token);
        await TopicSqlRf3Phase.AuthorityAsync(fixture, state, route, deadline.Token);
        await TopicSqlRf3Phase.HealthyAsync(fixture, state, route, deadline.Token);
        await ModelSqlRf3Cold.RunAsync(fixture, deadline.Token);
        await TopicSqlRf3Phase.HealthyAsync(fixture, state, route, deadline.Token);
        await ModelSqlRf3Cold.RunAsync(fixture, deadline.Token);
        await TopicSqlRf3Phase.HealthyAsync(fixture, state, route, deadline.Token);
    }
}
