using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

/// <summary>Proves F1 transfer intent, signed destination receipt and source completion through real RF3 callers.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class RemoteTransfersRf3Tests(ClusterFixture fixture)
{
    private const string InspectSourceTool = "keyload_queue_transfer_inspect";
    private const string InspectReceiptTool = "keyload_queue_transfer_receipt";
    private const string TransferMessageId = "rf3-transfer-message";
    private const Capability SourceCapabilities = Capability.QueuePublish | Capability.QueueInspect;
    private const Capability DestinationCapabilities = Capability.QueuePublish | Capability.QueueInspect
        | Capability.QueueConsume | Capability.QueueAck;

    [Test]
    public async Task AcXfer001To003SignedTransferSurvivesRevocationRetryAcknowledgementAndCompletion()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await MessagingRf3Scenario.CreateAsync(fixture, deadline.Token);
        var identity = await CreateTransferAdministratorAsync(fixture, scenario, deadline.Token);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, identity.Secret);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            identity.Secret, deadline.Token);
        var transferId = Guid.NewGuid();
        var message = new EnqueueMessage(scenario.DestinationQueue.Queue, TransferMessageId,
            MessagingRf3Scenario.ProtectedPayload, MessagingRf3Scenario.ProtectedHeaders);
        var create = Command(Guid.NewGuid(), scenario.SourcePartition,
            new CreateQueueTransfer(scenario.SourceQueue, transferId, scenario.DestinationQueue, message));
        _ = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(create, deadline.Token));
        var intent = await ReadSourceIntentAsync(sdk, mcp, scenario, transferId, identity.Secret, deadline.Token);

        await RejectTamperedIntentAsync(mcp, scenario, intent, deadline.Token);
        var revoked = await RejectRevokedDestinationPublisherAsync(sdk, mcp, scenario, identity, intent, transferId,
            deadline.Token);
        var restored = MessagingRf3Identity.WithCapability(revoked, scenario.DestinationQueue, DestinationCapabilities);
        await MessagingRf3Identity.UpdateAsync(fixture, restored, deadline.Token);

        var originalAccept = Command(Guid.NewGuid(), scenario.DestinationPartition,
            new AcceptQueueTransfer(scenario.DestinationQueue, intent.IntentToken));
        var accepted = await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await mcp.CallAsync(McpCallerTools.DocumentsCommit, originalAccept, deadline.Token));
        var receipt = await ReadDestinationReceiptAsync(sdk, mcp, scenario, transferId, identity.Secret, deadline.Token);
        var replayed = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(originalAccept, deadline.Token));
        await Assert.That(replayed).IsEqualTo(accepted.Value);

        await AcknowledgeTransferredMessageAsync(sdk, mcp, scenario, deadline.Token);
        await AcceptAfterAcknowledgementAsync(sdk, scenario, intent, transferId, receipt, deadline.Token);
        await CompleteSourceAsync(sdk, mcp, scenario, transferId, receipt, deadline.Token);
    }

    private static Task<MessagingRf3Identity> CreateTransferAdministratorAsync(ClusterFixture fixture,
        MessagingRf3Scenario scenario, CancellationToken cancellationToken)
    {
        var grants = new[]
        {
            Scope(scenario.SourceQueue, SourceCapabilities),
            Scope(scenario.DestinationQueue, DestinationCapabilities)
        };
        return MessagingRf3Identity.CreateAsync(fixture, scenario.SourcePartition.TenantId,
            grants, [MessagingRf3Scenario.WriteGrant, UseGrant], clusterAdministrator: true, cancellationToken);
    }

    private static ScopeGrant Scope(QueueLaneRef lane, Capability capabilities)
        => new(lane.Partition.DatabaseId, lane.Queue, capabilities);

    private static CommandRequest Command(Guid id, PartitionRef partition, Mutation mutation)
        => new(id, partition, [mutation]);

    private static async Task<QueueTransferInspection> ReadSourceIntentAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        MessagingRf3Scenario scenario, Guid transferId, string credential, CancellationToken cancellationToken)
    {
        var request = new InspectQueueTransferRequest(scenario.SourceQueue, transferId);
        var sdkView = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectQueueTransferAsync(request,
            cancellationToken));
        var reply = await mcp.CallAsync(InspectSourceTool, request, cancellationToken);
        var mcpView = await McpCallerAssertions.SuccessAsync<QueueTransferInspection?>(reply);
        await Assert.That(sdkView).IsNotNull();
        await Assert.That(sdkView!.State).IsEqualTo(QueueTransferState.OutputPending);
        await Assert.That(mcpView.Value).IsEqualTo(sdkView);
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, credential, MessagingRf3Scenario.ProtectedPayload);
        return sdkView;
    }

    private static async Task RejectTamperedIntentAsync(McpOfficialClient mcp, MessagingRf3Scenario scenario,
        QueueTransferInspection intent, CancellationToken cancellationToken)
    {
        var changed = intent.IntentToken[0] == 'A' ? 'B' : 'A';
        var tampered = string.Concat(changed.ToString(), intent.IntentToken[1..]);
        var command = Command(Guid.NewGuid(), scenario.DestinationPartition,
            new AcceptQueueTransfer(scenario.DestinationQueue, tampered));
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.DocumentsCommit, command,
            cancellationToken), ErrorCode.TokenInvalidated, dispatched: true);
    }

    private async Task<PrincipalRecord> RejectRevokedDestinationPublisherAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        MessagingRf3Scenario scenario, MessagingRf3Identity identity, QueueTransferInspection intent,
        Guid transferId, CancellationToken cancellationToken)
    {
        var revoked = MessagingRf3Identity.WithCapability(identity.Principal, scenario.DestinationQueue,
            DestinationCapabilities & ~Capability.QueuePublish);
        await MessagingRf3Identity.UpdateAsync(fixture, revoked, cancellationToken);
        var command = Command(Guid.NewGuid(), scenario.DestinationPartition,
            new AcceptQueueTransfer(scenario.DestinationQueue, intent.IntentToken));
        await AssertSdkFailureAsync(await sdk.CommitAsync(command, cancellationToken), ErrorCode.PermissionDenied);
        var message = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectAsync(
            new(scenario.DestinationQueue, TransferMessageId), cancellationToken));
        await Assert.That(message).IsNull();
        var request = new InspectQueueTransferReceiptRequest(scenario.DestinationQueue, scenario.SourceQueue, transferId);
        var absent = await McpCallerAssertions.SuccessAsync<QueueTransferReceiptInspection?>(
            await mcp.CallAsync(InspectReceiptTool, request, cancellationToken));
        await Assert.That(absent.Value).IsNull();
        return revoked;
    }

    private static async Task<QueueTransferReceiptInspection> ReadDestinationReceiptAsync(KeyLoadClient sdk,
        McpOfficialClient mcp, MessagingRf3Scenario scenario, Guid transferId,
        string credential, CancellationToken cancellationToken)
    {
        var request = new InspectQueueTransferReceiptRequest(scenario.DestinationQueue, scenario.SourceQueue, transferId);
        var sdkReceipt = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectQueueTransferReceiptAsync(request,
            cancellationToken));
        var reply = await mcp.CallAsync(InspectReceiptTool, request, cancellationToken);
        var mcpReceipt = await McpCallerAssertions.SuccessAsync<QueueTransferReceiptInspection?>(reply);
        await Assert.That(sdkReceipt).IsNotNull();
        await Assert.That(sdkReceipt!.TransferId).IsEqualTo(transferId);
        await Assert.That(mcpReceipt.Value).IsEqualTo(sdkReceipt);
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, credential, MessagingRf3Scenario.ProtectedPayload);
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, credential, MessagingRf3Scenario.ProtectedHeaders);
        return sdkReceipt;
    }

    private static async Task AcknowledgeTransferredMessageAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        MessagingRf3Scenario scenario, CancellationToken cancellationToken)
    {
        var received = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReceiveAsync(
            new(Guid.NewGuid(), scenario.DestinationQueue, LeaseSeconds: 60), cancellationToken));
        await Assert.That(received.Deliveries).HasSingleItem();
        var delivery = received.Deliveries[0];
        await Assert.That(delivery.Id).IsEqualTo(TransferMessageId);
        var ack = new DeliveryCommand(Guid.NewGuid(), scenario.DestinationQueue, delivery.Token, DeliveryAction.Ack);
        var mcpAck = await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await mcp.CallAsync(McpCallerTools.MessagesComplete, ack, cancellationToken));
        var sdkAck = await McpCallerAssertions.SdkSuccessAsync(await sdk.CompleteAsync(ack, cancellationToken));
        await Assert.That(sdkAck).IsEqualTo(mcpAck.Value);
        var persisted = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectAsync(
            new(scenario.DestinationQueue, TransferMessageId), cancellationToken));
        await Assert.That(persisted!.Metadata.State).IsEqualTo(MessageState.Acked);
    }

    private static async Task AcceptAfterAcknowledgementAsync(KeyLoadClient sdk, MessagingRf3Scenario scenario,
        QueueTransferInspection intent, Guid transferId, QueueTransferReceiptInspection receipt,
        CancellationToken cancellationToken)
    {
        var accept = Command(Guid.NewGuid(), scenario.DestinationPartition,
            new AcceptQueueTransfer(scenario.DestinationQueue, intent.IntentToken));
        _ = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(accept, cancellationToken));
        var current = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectQueueTransferReceiptAsync(
            new(scenario.DestinationQueue, scenario.SourceQueue, transferId), cancellationToken));
        await Assert.That(current).IsEqualTo(receipt);
        var persisted = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectAsync(
            new(scenario.DestinationQueue, TransferMessageId), cancellationToken));
        await Assert.That(persisted!.Metadata.State).IsEqualTo(MessageState.Acked);
        var next = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReceiveAsync(
            new(Guid.NewGuid(), scenario.DestinationQueue), cancellationToken));
        await Assert.That(next.Deliveries).IsEmpty();
    }

    private static async Task CompleteSourceAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        MessagingRf3Scenario scenario, Guid transferId, QueueTransferReceiptInspection receipt,
        CancellationToken cancellationToken)
    {
        var complete = Command(Guid.NewGuid(), scenario.SourcePartition,
            new CompleteQueueTransfer(scenario.SourceQueue, transferId, receipt.ReceiptToken));
        _ = await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await mcp.CallAsync(McpCallerTools.DocumentsCommit, complete, cancellationToken));
        var request = new InspectQueueTransferRequest(scenario.SourceQueue, transferId);
        var final = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectQueueTransferAsync(request,
            cancellationToken));
        await Assert.That(final!.State).IsEqualTo(QueueTransferState.Delivered);
        await Assert.That(final.ReceiptToken).IsEqualTo(receipt.ReceiptToken);
        var mcpFinal = await McpCallerAssertions.SuccessAsync<QueueTransferInspection?>(
            await mcp.CallAsync(InspectSourceTool, request, cancellationToken));
        await Assert.That(mcpFinal.Value).IsEqualTo(final);
    }

    private static async Task AssertSdkFailureAsync<T>(ManagedCode.Communication.Result<T> result,
        ErrorCode expected)
    {
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Problem?.ErrorCode).IsEqualTo(expected.ToString());
    }

    private static string UseGrant => string.Concat(MessagingRf3Scenario.SensitiveGrant, ".use");
}
