using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

/// <summary>AC-MSG-007: independent partial claims through actual SDK, official MCP and shared SQL CALL.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey), NotInParallel]
internal sealed class MultiLaneReceiveRf3Tests(ClusterFixture fixture)
{
    [Test]
    public async Task ThreeLanesExposeExactPartialOutcomesOnFourPublicPathsAndHealthyAck()
    {
        using var deadline = McpCallerDeadline.Create();
        using var adminHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var administrator = new KeyLoadClient(adminHttp, fixture.AdminKey, IntegrationClientOptions.Execution());
        var lanes = await MultiLaneReceiveRf3Flow.SeedAsync(administrator, deadline.Token);
        var identity = await MessagingRf3Identity.CreateAsync(fixture, lanes[0].Partition.TenantId,
            [new ScopeGrant("database", lanes[0].Queue, Capability.QueueConsume | Capability.QueueAck),
             new ScopeGrant("database", lanes[2].Queue, Capability.QueueConsume | Capability.QueueAck)],
            [], false, deadline.Token);
        using var workerHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var worker = new KeyLoadClient(workerHttp, identity.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            identity.Secret, deadline.Token);
        var tool = await mcp.Client.DiscoverKeyLoadToolAsync(MultiLaneReceiveRf3Flow.Tool, deadline.Token);
        await McpDiscoveryAssertions.VerifyAsync(tool);
        var deniedBefore = await McpCallerAssertions.SdkSuccessAsync(await administrator.InspectAsync(new(lanes[1], MultiLaneReceiveRf3Flow.Message), deadline.Token));
        var request = new MultiLaneReceiveRequest(Guid.NewGuid(), [.. lanes.Select(lane => new ReceiveRequest(Guid.NewGuid(), lane))]);
        var original = await McpCallerAssertions.SdkSuccessAsync(await worker.ReceiveAcrossLanesAsync(request, deadline.Token));
        await Assert.That(original.RequestId).IsEqualTo(request.RequestId);
        await Assert.That(original.Outcomes.Length).IsEqualTo(3);
        await MultiLaneReceiveRf3Flow.CommittedAsync(administrator, original.Outcomes[0], request.Requests[0], deadline.Token);
        await MultiLaneReceiveRf3Flow.CommittedAsync(administrator, original.Outcomes[2], request.Requests[2], deadline.Token);
        await AssertDeniedAsync(original.Outcomes[1], request.Requests[1]);
        var native = await McpCallerAssertions.SuccessAsync<MultiLaneReceiveResult>(await mcp.CallAsync(
            MultiLaneReceiveRf3Flow.Tool, request, deadline.Token));
        await MultiLaneReceiveRf3Flow.EquivalentAsync(native.Value, original);
        var sql = await McpCallerAssertions.SdkSuccessAsync(await worker.ExecuteSqlAsync(MultiLaneReceiveRf3Flow.Sql(request), deadline.Token));
        await MultiLaneReceiveRf3Flow.EquivalentAsync(sql.Deserialize<MultiLaneReceiveResult>(JsonDefaults.Options)!, original);
        var sqlMcp = await McpCallerAssertions.SuccessAsync<MultiLaneReceiveResult>(await mcp.CallAsync(
            SqlOperationProtocol.ToolName, MultiLaneReceiveRf3Flow.Sql(request), deadline.Token));
        await MultiLaneReceiveRf3Flow.EquivalentAsync(sqlMcp.Value, original);
        var deniedAfter = await McpCallerAssertions.SdkSuccessAsync(await administrator.InspectAsync(new(lanes[1], MultiLaneReceiveRf3Flow.Message), deadline.Token));
        await Assert.That(JsonDefaults.Serialize(deniedAfter).AsSpan().SequenceEqual(JsonDefaults.Serialize(deniedBefore))).IsTrue();
        await CompleteAndFollowAsync(administrator, worker, mcp, request, original, deadline.Token);
    }

    private static async Task AssertDeniedAsync(QueueLaneReceiveOutcome denied, ReceiveRequest request)
    {
        await Assert.That(denied.RequestId).IsEqualTo(request.RequestId);
        await Assert.That(denied.Lane).IsEqualTo(request.Lane);
        await Assert.That(denied.Status).IsEqualTo(QueueLaneReceiveStatus.Rejected);
        await Assert.That(denied.Result).IsNull();
        await Assert.That(denied.Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(denied.SafeDetail).IsEqualTo("The principal cannot perform this operation in this scope.");
    }

    private static async Task CompleteAndFollowAsync(KeyLoadClient administrator, KeyLoadClient worker,
        McpOfficialClient mcp, MultiLaneReceiveRequest request, MultiLaneReceiveResult original, CancellationToken token)
    {
        foreach (var index in new[] { 0, 2 })
        {
            var lane = request.Requests[index].Lane;
            var delivery = original.Outcomes[index].Result!.Deliveries[0];
            var ack = new DeliveryCommand(Guid.NewGuid(), lane, delivery.Token, DeliveryAction.Ack);
            var completed = await McpCallerAssertions.SdkSuccessAsync(await worker.CompleteAsync(ack, token));
            var replay = await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(McpCallerTools.MessagesComplete, ack, token));
            await Assert.That(JsonDefaults.Serialize(completed).AsSpan().SequenceEqual(JsonDefaults.Serialize(replay.Value))).IsTrue();
            var after = await McpCallerAssertions.SdkSuccessAsync(await administrator.InspectAsync(new(lane, MultiLaneReceiveRf3Flow.Message), token));
            await Assert.That(after!.Metadata.State).IsEqualTo(MessageState.Acked);
        }
        var healthyLane = request.Requests[0].Lane;
        await McpCallerAssertions.SdkSuccessAsync(await administrator.CommitAsync(new(Guid.NewGuid(), healthyLane.Partition,
            [new EnqueueMessage(healthyLane.Queue, "healthy", "{\"work\":2}", "{\"kind\":\"next\"}")]), token));
        var healthy = await McpCallerAssertions.SdkSuccessAsync(await worker.ReceiveAcrossLanesAsync(
            new(Guid.NewGuid(), [new(Guid.NewGuid(), healthyLane)]), token));
        await Assert.That(healthy.Outcomes.Length).IsEqualTo(1);
        await Assert.That(healthy.Outcomes[0].Status).IsEqualTo(QueueLaneReceiveStatus.Committed);
        var message = await Assert.That(healthy.Outcomes[0].Result!.Deliveries).HasSingleItem();
        await Assert.That(message.Id).IsEqualTo("healthy");
        await Assert.That(message.PayloadJson).IsEqualTo("{\"work\":2}");
        await Assert.That(message.HeadersJson).IsEqualTo("{\"kind\":\"next\"}");
        await Assert.That(message.Attempt).IsEqualTo(1);
        await Assert.That(message.LeaseVersion).IsEqualTo(1L);
        await Assert.That(message.DeliveryGeneration).IsEqualTo(1L);
    }
}
