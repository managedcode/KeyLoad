using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class TopicRetentionRf3Tests(ClusterFixture fixture)
{
    [Test]
    public async Task AcEventRetention003ActualSdkMcpAndSqlRetainPinnedHistoryAndCanonicalIdentity()
    {
        using var deadline = McpCallerDeadline.Create();
        var token = deadline.Token;
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        var scenario = await TopicRetentionRf3Scenario.CreateAsync(sdk, token);
        await using var session = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2, fixture.AdminKey, token);
        await McpCallerAssertions.ErrorAsync(await session.CallAsync(McpCallerTools.DocumentsCommit, scenario.Purge(), token),
            ErrorCode.ResourceExhausted, dispatched: true);
        await McpCallerAssertions.SdkSuccessAsync(await sdk.SeekSubscriptionAsync(
            new(Guid.NewGuid(), scenario.Subscription, 1, SubscriptionStart.FromNow), token));
        var command = scenario.Purge();
        var purged = await McpCallerAssertions.SuccessAsync<CommitReceipt>(await session.CallAsync(McpCallerTools.DocumentsCommit, command, token));
        await Assert.That(NativeSerialization.Serialize(purged.Value.Mutations.Single()).SequenceEqual(
            NativeSerialization.Serialize(new MutationReceipt("purgeTopic", "topic", "2", 2)))).IsTrue();
        var replay = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(command, token));
        await Assert.That(JsonDefaults.Serialize(replay).AsSpan().SequenceEqual(JsonDefaults.Serialize(purged.Value))).IsTrue();
        var original = await McpCallerAssertions.SuccessAsync<CommitReceipt>(await session.CallAsync(
            McpCallerTools.DocumentsCommit, scenario.Publication, token));
        await Assert.That(JsonDefaults.Serialize(original.Value).AsSpan().SequenceEqual(JsonDefaults.Serialize(scenario.Receipt))).IsTrue();
        await TopicRetentionRf3Assertions.RetainedAsync(scenario, sdk, session, token);
        await VerifyContinuationAsync(scenario, sdk, session, token);
    }

    private static async Task VerifyContinuationAsync(TopicRetentionRf3Scenario scenario, KeyLoadClient sdk,
        McpOfficialClient session, CancellationToken token)
    {
        foreach (var item in new[] { ("{\"n\":1}", ErrorCode.DuplicateEventId), ("{\"n\":99}", ErrorCode.Conflict) })
        {
            var duplicate = new CommandRequest(Guid.NewGuid(), scenario.Partition,
                [new PublishTopic("topic", [new("event1", "Created", item.Item1)])]);
            await McpCallerAssertions.ErrorAsync(await session.CallAsync(McpCallerTools.DocumentsCommit, duplicate, token), item.Item2, dispatched: true);
        }
        await TopicRetentionRf3Assertions.RetainedAsync(scenario, sdk, session, token);
        var healthySql = scenario.HealthySql();
        var healthyReceipt = await SqlRf3Protocol.SdkAsync<CommitReceipt>(sdk, healthySql, token);
        var healthyCommand = healthySql.Parameters![TopicRetentionRf3Scenario.ArgumentsKey]
            .GetProperty(TopicRetentionRf3Scenario.RequestKey).Deserialize<CommandRequest>(JsonDefaults.Options)!;
        await Assert.That(healthyReceipt.CommandId).IsEqualTo(healthyCommand.CommandId);
        await Assert.That(NativeSerialization.Serialize(healthyReceipt.Mutations.Single()).SequenceEqual(
            NativeSerialization.Serialize(new MutationReceipt("publishTopic", "topic", "event4", 4)))).IsTrue();
        var sqlReplay = await SqlRf3Protocol.McpAsync<CommitReceipt>(session, healthySql, token);
        await Assert.That(JsonDefaults.Serialize(sqlReplay).AsSpan().SequenceEqual(JsonDefaults.Serialize(healthyReceipt))).IsTrue();
        var healthy = await McpCallerAssertions.SuccessAsync<EventSourcePage>(await session.CallAsync(McpCallerTools.EventsRead,
            new ReadEventSourceRequest(scenario.Source, AfterPosition: 3), token));
        await Assert.That(healthy.Value.Events.Single().Position).IsEqualTo(4L);
        await Assert.That(healthy.Value.Events.Single().EventSequence).IsEqualTo(4L);
        await Assert.That(healthy.Value.Events.Single().Data).IsEqualTo(new EventData("event4", "Created", "{\"n\":4}"));
    }
}
