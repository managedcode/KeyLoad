using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

/// <summary>AC-AISQL-006: genuine SQL callers preserve queue delivery identities, fencing and acknowledgement effects.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class SqlRf3DeliveryTests(ClusterFixture fixture)
{
    private const string InvalidToken = "invalid-delivery-token";

    [Test]
    public async Task AcAisql006SqlReceiveAndAckPreserveStableIdsAndRejectForgedTokensWithoutEffects()
    {
        using var deadline = McpCallerDeadline.Create();
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, fixture.AdminKey);
        var scenario = await SqlRf3DeliveryScenario.CreateAsync(sdk, deadline.Token);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, fixture.AdminKey, deadline.Token);
        var receive = scenario.Receive(Guid.NewGuid());
        var sql = SqlRf3Protocol.Call(scenario.Partition, McpCallerTools.MessagesReceive, receive);
        var result = await SqlRf3Protocol.SdkAsync<ReceiveResult>(sdk, sql, deadline.Token);
        await Assert.That(result.RequestId).IsEqualTo(receive.RequestId);
        await Assert.That(result.Deliveries).HasSingleItem();
        var delivery = result.Deliveries[0];
        await Assert.That(delivery.Id).IsEqualTo(SqlRf3DeliveryScenario.MessageId);
        await Assert.That(delivery.PayloadJson).IsEqualTo(SqlRf3DeliveryScenario.Payload);
        await Assert.That(delivery.HeadersJson).IsEqualTo(SqlRf3DeliveryScenario.Headers);
        await Assert.That(delivery.Attempt).IsEqualTo(SqlRf3DeliveryScenario.FirstAttempt);
        await Assert.That(string.IsNullOrEmpty(delivery.Token)).IsFalse();
        await SqlRf3Protocol.EqualAsync(result, await McpCallerAssertions.SdkSuccessAsync(await sdk.ReceiveAsync(receive, deadline.Token)));
        await SqlRf3Protocol.EqualAsync(result, await SqlRf3Protocol.McpAsync<ReceiveResult>(mcp, sql, deadline.Token));
        await VerifyNoDeliveryAsync(mcp, scenario, deadline.Token);
        var leased = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectAsync(scenario.Inspect, deadline.Token));
        await Assert.That(leased!.Metadata.State).IsEqualTo(MessageState.Leased);
        await Assert.That(leased.Metadata.LeaseVersion).IsEqualTo(delivery.LeaseVersion);
        await Assert.That(leased.Metadata.DeliveryGeneration).IsEqualTo(delivery.DeliveryGeneration);
        await VerifyFencingAsync(sdk, mcp, scenario, leased, deadline.Token);
        await VerifyPrincipalFenceAsync(sdk, scenario, delivery, leased, deadline.Token);
        await VerifyAcknowledgementAsync(sdk, mcp, scenario, delivery, leased.Metadata, deadline.Token);
    }

    private static async Task VerifyFencingAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        SqlRf3DeliveryScenario scenario, MessageInspection leased, CancellationToken cancellationToken)
    {
        var command = new DeliveryCommand(Guid.NewGuid(), scenario.Lane, InvalidToken, DeliveryAction.Ack);
        var sql = SqlRf3Protocol.Call(scenario.Partition, McpCallerTools.MessagesComplete, command);
        var rejected = await sdk.ExecuteSqlAsync(sql, cancellationToken);
        await Assert.That(rejected.IsSuccess).IsFalse();
        await Assert.That(rejected.Problem!.ErrorCode).IsEqualTo(nameof(ErrorCode.TokenInvalidated));
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(SqlOperationProtocol.ToolName, sql, cancellationToken),
            ErrorCode.TokenInvalidated, dispatched: true);
        await SqlRf3Protocol.EqualAsync(leased, await McpCallerAssertions.SdkSuccessAsync(
            await sdk.InspectAsync(scenario.Inspect, cancellationToken)));
        await SqlRf3Protocol.EqualAsync(leased, await SqlRf3Protocol.McpAsync<MessageInspection>(mcp,
            SqlRf3Protocol.Call(scenario.Partition, McpCallerTools.MessagesInspect, scenario.Inspect), cancellationToken));
    }

    private async Task VerifyPrincipalFenceAsync(KeyLoadClient administrator, SqlRf3DeliveryScenario scenario,
        Delivery delivery, MessageInspection leased, CancellationToken cancellationToken)
    {
        var identity = await McpPersistedIdentity.CreateAsync(fixture, scenario.Partition, SqlRf3DeliveryScenario.Queue,
            Capability.QueueAck, cancellationToken);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var foreignSdk = new KeyLoadClient(http, identity.Secret);
        await using var foreignMcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2,
            identity.Secret, cancellationToken);
        var command = new DeliveryCommand(Guid.NewGuid(), scenario.Lane, delivery.Token, DeliveryAction.Ack);
        var sql = SqlRf3Protocol.Call(scenario.Partition, McpCallerTools.MessagesComplete, command);
        var rejected = await foreignSdk.ExecuteSqlAsync(sql, cancellationToken);
        await Assert.That(rejected.IsSuccess).IsFalse();
        await Assert.That(rejected.Problem!.ErrorCode).IsEqualTo(nameof(ErrorCode.TokenInvalidated));
        await McpCallerAssertions.ErrorAsync(await foreignMcp.CallAsync(SqlOperationProtocol.ToolName, sql, cancellationToken),
            ErrorCode.TokenInvalidated, dispatched: true);
        await SqlRf3Protocol.EqualAsync(leased, await McpCallerAssertions.SdkSuccessAsync(
            await administrator.InspectAsync(scenario.Inspect, cancellationToken)));
    }

    private static async Task VerifyAcknowledgementAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        SqlRf3DeliveryScenario scenario, Delivery delivery, MessageMetadata leased, CancellationToken cancellationToken)
    {
        var command = new DeliveryCommand(Guid.NewGuid(), scenario.Lane, delivery.Token, DeliveryAction.Ack);
        var sql = SqlRf3Protocol.Call(scenario.Partition, McpCallerTools.MessagesComplete, command);
        var receipt = await SqlRf3Protocol.McpAsync<CommitReceipt>(mcp, sql, cancellationToken);
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Mutations).HasSingleItem();
        await Assert.That(receipt.Mutations[0]).IsEqualTo(new MutationReceipt(nameof(DeliveryAction.Ack),
            SqlRf3DeliveryScenario.Queue, SqlRf3DeliveryScenario.MessageId, leased.StateVersion + McpCallerProtocol.EpochIncrement));
        await SqlRf3Protocol.EqualAsync(receipt, await McpCallerAssertions.SdkSuccessAsync(await sdk.CompleteAsync(command, cancellationToken)));
        await SqlRf3Protocol.EqualAsync(receipt, await SqlRf3Protocol.SdkAsync<CommitReceipt>(sdk, sql, cancellationToken));
        var acknowledged = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectAsync(scenario.Inspect, cancellationToken));
        await Assert.That(acknowledged!.Metadata).IsEqualTo(leased with
        {
            State = MessageState.Acked,
            StateVersion = leased.StateVersion + McpCallerProtocol.EpochIncrement,
            LeaseOwner = null,
            LeaseUntil = null
        });
        await Assert.That(acknowledged.PayloadJson).IsNull();
        await Assert.That(acknowledged.HeadersJson).IsNull();
        await SqlRf3Protocol.EqualAsync(acknowledged, await SqlRf3Protocol.McpAsync<MessageInspection>(mcp,
            SqlRf3Protocol.Call(scenario.Partition, McpCallerTools.MessagesInspect, scenario.Inspect), cancellationToken));
        await VerifyNoDeliveryAsync(mcp, scenario, cancellationToken);
    }

    private static async Task VerifyNoDeliveryAsync(McpOfficialClient mcp, SqlRf3DeliveryScenario scenario,
        CancellationToken cancellationToken)
    {
        var request = scenario.Receive(Guid.NewGuid());
        var result = await SqlRf3Protocol.McpAsync<ReceiveResult>(mcp,
            SqlRf3Protocol.Call(scenario.Partition, McpCallerTools.MessagesReceive, request), cancellationToken);
        await Assert.That(result.RequestId).IsEqualTo(request.RequestId);
        await Assert.That(result.Deliveries).IsEmpty();
    }
}
