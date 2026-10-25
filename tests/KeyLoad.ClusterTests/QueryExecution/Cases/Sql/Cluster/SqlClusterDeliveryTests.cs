using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.QueryExecution;

/// <summary>AC-AISQL-006: genuine SQL callers preserve queue delivery identities, fencing and acknowledgement effects.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class SqlClusterDeliveryTests(ClusterFixture fixture)
{
    private const string InvalidToken = "invalid-delivery-token";

    [Test]
    public async Task AcAisql006SqlReceiveAndAckPreserveStableIdsAndRejectForgedTokensWithoutEffects()
    {
        using var deadline = McpCallerDeadline.Create();
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        var scenario = await SqlClusterDeliveryScenario.CreateAsync(sdk, deadline.Token);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, fixture.AdminKey, deadline.Token);
        var receive = scenario.Receive(Guid.NewGuid());
        var sql = SqlClusterProtocol.Call(scenario.Partition, McpCallerTools.MessagesReceive, receive);
        var result = await SqlClusterProtocol.SdkAsync<ReceiveResult>(sdk, sql, deadline.Token);
        await Assert.That(result.RequestId).IsEqualTo(receive.RequestId);
        await Assert.That(result.Deliveries).HasSingleItem();
        var delivery = result.Deliveries[0];
        await Assert.That(delivery.Id).IsEqualTo(SqlClusterDeliveryScenario.MessageId);
        await Assert.That(delivery.PayloadJson).IsEqualTo(SqlClusterDeliveryScenario.Payload);
        await Assert.That(delivery.HeadersJson).IsEqualTo(SqlClusterDeliveryScenario.Headers);
        await Assert.That(delivery.Attempt).IsEqualTo(SqlClusterDeliveryScenario.FirstAttempt);
        await Assert.That(string.IsNullOrEmpty(delivery.Token)).IsFalse();
        await SqlClusterProtocol.EqualAsync(result, await McpCallerAssertions.SdkSuccessAsync(await sdk.ReceiveAsync(receive, deadline.Token)));
        await SqlClusterProtocol.EqualAsync(result, await SqlClusterProtocol.McpAsync<ReceiveResult>(mcp, sql, deadline.Token));
        await VerifyNoDeliveryAsync(mcp, scenario, deadline.Token);
        var leased = await PublicReadAssertions.MessageAsync(await sdk.InspectAsync(scenario.Inspect, deadline.Token));
        await Assert.That(leased!.Metadata.State).IsEqualTo(MessageState.Leased);
        await Assert.That(leased.Metadata.LeaseVersion).IsEqualTo(delivery.LeaseVersion);
        await Assert.That(leased.Metadata.DeliveryGeneration).IsEqualTo(delivery.DeliveryGeneration);
        await VerifyFencingAsync(sdk, mcp, scenario, leased, deadline.Token);
        await VerifyPrincipalFenceAsync(sdk, scenario, delivery, leased, deadline.Token);
        await VerifyAcknowledgementAsync(sdk, mcp, scenario, delivery, leased.Metadata, deadline.Token);
    }

    private static async Task VerifyFencingAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        SqlClusterDeliveryScenario scenario, MessageInspection leased, CancellationToken cancellationToken)
    {
        var command = new DeliveryCommand(Guid.NewGuid(), scenario.Lane, InvalidToken, DeliveryAction.Ack);
        var sql = SqlClusterProtocol.Call(scenario.Partition, McpCallerTools.MessagesComplete, command);
        var rejected = await sdk.ExecuteSqlAsync(sql, cancellationToken);
        await Assert.That(rejected.IsSuccess).IsFalse();
        await Assert.That(rejected.Problem!.ErrorCode).IsEqualTo(nameof(ErrorCode.TokenInvalidated));
        _ = (await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(SqlOperationProtocol.ToolName, sql, cancellationToken),
            ErrorCode.TokenInvalidated, dispatched: true)).Value;
        await SqlClusterProtocol.EqualAsync(leased, await PublicReadAssertions.MessageAsync(
            await sdk.InspectAsync(scenario.Inspect, cancellationToken)));
        await SqlClusterProtocol.EqualAsync(leased, (await SqlClusterProtocol.McpAsync<MessageInspectionResult>(mcp,
            SqlClusterProtocol.Call(scenario.Partition, McpCallerTools.MessagesInspect, scenario.Inspect), cancellationToken)).Message);
    }

    private async Task VerifyPrincipalFenceAsync(KeyLoadClient administrator, SqlClusterDeliveryScenario scenario,
        Delivery delivery, MessageInspection leased, CancellationToken cancellationToken)
    {
        var identity = await McpPersistedIdentity.CreateAsync(fixture, scenario.Partition, SqlClusterDeliveryScenario.Queue,
            Capability.QueueAck, cancellationToken);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var foreignSdk = new KeyLoadClient(http, identity.Secret, IntegrationClientOptions.Execution());
        await using var foreignMcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2,
            identity.Secret, cancellationToken);
        var command = new DeliveryCommand(Guid.NewGuid(), scenario.Lane, delivery.Token, DeliveryAction.Ack);
        var sql = SqlClusterProtocol.Call(scenario.Partition, McpCallerTools.MessagesComplete, command);
        var rejected = await foreignSdk.ExecuteSqlAsync(sql, cancellationToken);
        await Assert.That(rejected.IsSuccess).IsFalse();
        await Assert.That(rejected.Problem!.ErrorCode).IsEqualTo(nameof(ErrorCode.TokenInvalidated));
        _ = (await McpCallerAssertions.ErrorAsync(await foreignMcp.CallAsync(SqlOperationProtocol.ToolName, sql, cancellationToken),
            ErrorCode.TokenInvalidated, dispatched: true)).Value;
        await SqlClusterProtocol.EqualAsync(leased, await PublicReadAssertions.MessageAsync(
            await administrator.InspectAsync(scenario.Inspect, cancellationToken)));
    }

    private static async Task VerifyAcknowledgementAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        SqlClusterDeliveryScenario scenario, Delivery delivery, MessageMetadata leased, CancellationToken cancellationToken)
    {
        var command = new DeliveryCommand(Guid.NewGuid(), scenario.Lane, delivery.Token, DeliveryAction.Ack);
        var sql = SqlClusterProtocol.Call(scenario.Partition, McpCallerTools.MessagesComplete, command);
        var receipt = await SqlClusterProtocol.McpAsync<CommitReceipt>(mcp, sql, cancellationToken);
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Mutations).HasSingleItem();
        await Assert.That(receipt.Mutations[0]).IsEqualTo(new MutationReceipt(nameof(DeliveryAction.Ack),
            SqlClusterDeliveryScenario.Queue, SqlClusterDeliveryScenario.MessageId, leased.StateVersion + McpCallerProtocol.EpochIncrement));
        await SqlClusterProtocol.EqualAsync(receipt, await McpCallerAssertions.SdkSuccessAsync(await sdk.CompleteAsync(command, cancellationToken)));
        await SqlClusterProtocol.EqualAsync(receipt, await SqlClusterProtocol.SdkAsync<CommitReceipt>(sdk, sql, cancellationToken));
        var acknowledged = await PublicReadAssertions.MessageAsync(await sdk.InspectAsync(scenario.Inspect, cancellationToken));
        await Assert.That(acknowledged!.Metadata).IsEqualTo(leased with
        {
            State = MessageState.Acked,
            StateVersion = leased.StateVersion + McpCallerProtocol.EpochIncrement,
            LeaseOwner = null,
            LeaseUntil = null
        });
        await Assert.That(acknowledged.PayloadJson).IsNull();
        await Assert.That(acknowledged.HeadersJson).IsNull();
        await SqlClusterProtocol.EqualAsync(acknowledged, (await SqlClusterProtocol.McpAsync<MessageInspectionResult>(mcp,
            SqlClusterProtocol.Call(scenario.Partition, McpCallerTools.MessagesInspect, scenario.Inspect), cancellationToken)).Message);
        await VerifyNoDeliveryAsync(mcp, scenario, cancellationToken);
    }

    private static async Task VerifyNoDeliveryAsync(McpOfficialClient mcp, SqlClusterDeliveryScenario scenario,
        CancellationToken cancellationToken)
    {
        var request = scenario.Receive(Guid.NewGuid());
        var result = await SqlClusterProtocol.McpAsync<ReceiveResult>(mcp,
            SqlClusterProtocol.Call(scenario.Partition, McpCallerTools.MessagesReceive, request), cancellationToken);
        await Assert.That(result.RequestId).IsEqualTo(request.RequestId);
        await Assert.That(result.Deliveries).IsEmpty();
    }
}
