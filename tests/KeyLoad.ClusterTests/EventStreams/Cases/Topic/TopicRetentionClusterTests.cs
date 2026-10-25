using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.QueryExecution;

namespace KeyLoad.ClusterTests.EventStreams;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class TopicRetentionClusterTests(ClusterFixture fixture)
{
    [Test]
    public async Task AcEventRetention003ActualSdkMcpAndSqlRetainPinnedHistoryAndCanonicalIdentity()
    {
        using var deadline = McpCallerDeadline.Create();
        var token = deadline.Token;
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        var scenario = await TopicRetentionClusterScenario.CreateAsync(sdk, token);
        await using var session = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2, fixture.AdminKey, token);
        _ = (await McpCallerAssertions.ErrorAsync(await session.CallAsync(McpCallerTools.DocumentsCommit, scenario.Purge(), token),
            ErrorCode.ResourceExhausted, dispatched: true)).Value;
        await McpCallerAssertions.SdkSuccessAsync(await sdk.SeekSubscriptionAsync(
            new(Guid.NewGuid(), scenario.Subscription, 1, SubscriptionStart.FromNow), token));
        var command = scenario.Purge();
        var purged = await McpCallerAssertions.SuccessAsync<CommitReceipt>(await session.CallAsync(McpCallerTools.DocumentsCommit, command, token));
        await Assert.That(OrleansSerialization.Serialize(purged.Value.Mutations.Single()).SequenceEqual(
            OrleansSerialization.Serialize(new MutationReceipt("purgeTopic", "topic", "2", 2)))).IsTrue();
        var replay = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(command, token));
        await Assert.That(JsonDefaults.Serialize(replay).AsSpan().SequenceEqual(JsonDefaults.Serialize(purged.Value))).IsTrue();
        var original = await McpCallerAssertions.SuccessAsync<CommitReceipt>(await session.CallAsync(
            McpCallerTools.DocumentsCommit, scenario.Publication, token));
        await Assert.That(JsonDefaults.Serialize(original.Value).AsSpan().SequenceEqual(JsonDefaults.Serialize(scenario.Receipt))).IsTrue();
        await TopicRetentionClusterAssertions.RetainedAsync(scenario, sdk, session, token);
        await VerifyContinuationAsync(scenario, sdk, session, token);
    }

    private static async Task VerifyContinuationAsync(TopicRetentionClusterScenario scenario, KeyLoadClient sdk,
        McpOfficialClient session, CancellationToken token)
    {
        foreach (var item in new[] { ("{\"n\":1}", ErrorCode.DuplicateEventId), ("{\"n\":99}", ErrorCode.Conflict) })
        {
            var duplicate = new CommandRequest(Guid.NewGuid(), scenario.Partition,
                [new PublishTopic("topic", [new("event1", "Created", item.Item1)])]);
            _ = (await McpCallerAssertions.ErrorAsync(await session.CallAsync(McpCallerTools.DocumentsCommit, duplicate, token), item.Item2, dispatched: true)).Value;
        }
        await TopicRetentionClusterAssertions.RetainedAsync(scenario, sdk, session, token);
        var healthySql = scenario.HealthySql();
        var healthyReceipt = await SqlClusterProtocol.SdkAsync<CommitReceipt>(sdk, healthySql, token);
        var healthyCommand = healthySql.Parameters![TopicRetentionClusterScenario.ArgumentsKey]
            .GetProperty(TopicRetentionClusterScenario.RequestKey).Deserialize<CommandRequest>(JsonDefaults.Options)!;
        await Assert.That(healthyReceipt.CommandId).IsEqualTo(healthyCommand.CommandId);
        await Assert.That(OrleansSerialization.Serialize(healthyReceipt.Mutations.Single()).SequenceEqual(
            OrleansSerialization.Serialize(new MutationReceipt("publishTopic", "topic", "event4", 4)))).IsTrue();
        var sqlReplay = await SqlClusterProtocol.McpAsync<CommitReceipt>(session, healthySql, token);
        await Assert.That(JsonDefaults.Serialize(sqlReplay).AsSpan().SequenceEqual(JsonDefaults.Serialize(healthyReceipt))).IsTrue();
        var healthy = await McpCallerAssertions.SuccessAsync<EventSourcePage>(await session.CallAsync(McpCallerTools.EventsRead,
            new ReadEventSourceRequest(scenario.Source, AfterPosition: 3), token));
        await Assert.That(healthy.Value.Events.Single().Position).IsEqualTo(4L);
        await Assert.That(healthy.Value.Events.Single().EventSequence).IsEqualTo(4L);
        await Assert.That(healthy.Value.Events.Single().Data).IsEqualTo(new EventData("event4", "Created", "{\"n\":4}"));
    }
}
