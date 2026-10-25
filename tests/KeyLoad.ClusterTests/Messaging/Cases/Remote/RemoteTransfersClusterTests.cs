using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.Messaging;

/// <summary>Proves F1 transfer intent, signed destination receipt and source completion through real Orleans callers.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class RemoteTransfersClusterTests(ClusterFixture fixture)
{
    private const string InspectSourceTool = "keyload_queue_transfer_inspect";
    private const string InspectReceiptTool = "keyload_queue_transfer_receipt";
    private const string TransferMessageId = "cluster-transfer-message";
    private const Capability SourceCapabilities = Capability.QueuePublish | Capability.QueueInspect;
    private const Capability DestinationCapabilities = Capability.QueuePublish | Capability.QueueInspect
        | Capability.QueueConsume | Capability.QueueAck;

    [Test]
    public async Task AcXfer001To003SignedTransferSurvivesRevocationRetryAcknowledgementAndCompletion()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await MessagingClusterScenario.CreateAsync(fixture, deadline.Token);
        var identity = await CreateTransferAdministratorAsync(fixture, scenario, deadline.Token);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, identity.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            identity.Secret, deadline.Token);
        var transferId = Guid.NewGuid();
        var message = new EnqueueMessage(scenario.DestinationQueue.Queue, TransferMessageId,
            MessagingClusterScenario.ProtectedPayload, MessagingClusterScenario.ProtectedHeaders);
        var create = Command(Guid.NewGuid(), scenario.SourcePartition,
            new CreateQueueTransfer(scenario.SourceQueue, transferId, scenario.DestinationQueue, message));
        _ = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(create, deadline.Token));
        var intent = await ReadSourceIntentAsync(sdk, mcp, scenario, transferId, identity.Secret, deadline.Token);

        await RejectTamperedIntentAsync(mcp, scenario, intent, deadline.Token);
        var revoked = await RejectRevokedDestinationPublisherAsync(sdk, mcp, scenario, identity, intent, transferId,
            deadline.Token);
        var restored = MessagingClusterIdentity.WithCapability(revoked, scenario.DestinationQueue, DestinationCapabilities);
        await MessagingClusterIdentity.UpdateAsync(fixture, restored, deadline.Token);

        var originalAccept = Command(Guid.NewGuid(), scenario.DestinationPartition,
            new AcceptQueueTransfer(scenario.DestinationQueue, intent.IntentToken));
        var accepted = await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await mcp.CallAsync(McpCallerTools.DocumentsCommit, originalAccept, deadline.Token));
        var receipt = await ReadDestinationReceiptAsync(sdk, mcp, scenario, transferId, identity.Secret, deadline.Token);
        var replayed = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(originalAccept, deadline.Token));
        await Assert.That(JsonDefaults.Serialize(replayed).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(accepted.Value))).IsTrue();

        await AcknowledgeTransferredMessageAsync(sdk, mcp, scenario, deadline.Token);
        await AcceptAfterAcknowledgementAsync(sdk, scenario, intent, transferId, receipt, deadline.Token);
        await CompleteSourceAsync(sdk, mcp, scenario, transferId, receipt, deadline.Token);
    }

    private static Task<MessagingClusterIdentity> CreateTransferAdministratorAsync(ClusterFixture fixture,
        MessagingClusterScenario scenario, CancellationToken cancellationToken)
    {
        var grants = new[]
        {
            Scope(scenario.SourceQueue, SourceCapabilities),
            Scope(scenario.DestinationQueue, DestinationCapabilities)
        };
        return MessagingClusterIdentity.CreateAsync(fixture, scenario.SourcePartition.TenantId,
            grants, [MessagingClusterScenario.WriteGrant, UseGrant], clusterAdministrator: true, cancellationToken);
    }

    private static ScopeGrant Scope(QueueLaneRef lane, Capability capabilities)
        => new(lane.Partition.DatabaseId, lane.Queue, capabilities);

    private static CommandRequest Command(Guid id, PartitionRef partition, Mutation mutation)
        => new(id, partition, [mutation]);

    private static async Task<QueueTransferInspection> ReadSourceIntentAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        MessagingClusterScenario scenario, Guid transferId, string credential, CancellationToken cancellationToken)
    {
        var request = new InspectQueueTransferRequest(scenario.SourceQueue, transferId);
        var sdkResponse = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectQueueTransferAsync(request,
            cancellationToken));
        var sdkView = sdkResponse.Transfer;
        var reply = await mcp.CallAsync(InspectSourceTool, request, cancellationToken);
        var mcpView = await McpCallerAssertions.SuccessAsync<QueueTransferReadResult>(reply);
        await Assert.That(sdkView).IsNotNull();
        await Assert.That(sdkView!.State).IsEqualTo(QueueTransferState.OutputPending);
        await Assert.That(mcpView.Value.Transfer).IsEqualTo(sdkView);
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, credential, MessagingClusterScenario.ProtectedPayload);
        return sdkView;
    }

    private static async Task RejectTamperedIntentAsync(McpOfficialClient mcp, MessagingClusterScenario scenario,
        QueueTransferInspection intent, CancellationToken cancellationToken)
    {
        var changed = intent.IntentToken[0] == 'A' ? 'B' : 'A';
        var tampered = string.Concat(changed.ToString(), intent.IntentToken[1..]);
        var command = Command(Guid.NewGuid(), scenario.DestinationPartition,
            new AcceptQueueTransfer(scenario.DestinationQueue, tampered));
        _ = (await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.DocumentsCommit, command,
            cancellationToken), ErrorCode.TokenInvalidated, dispatched: true)).Value;
    }

    private async Task<PrincipalRecord> RejectRevokedDestinationPublisherAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        MessagingClusterScenario scenario, MessagingClusterIdentity identity, QueueTransferInspection intent,
        Guid transferId, CancellationToken cancellationToken)
    {
        var revoked = MessagingClusterIdentity.WithCapability(identity.Principal, scenario.DestinationQueue,
            DestinationCapabilities & ~Capability.QueuePublish);
        await MessagingClusterIdentity.UpdateAsync(fixture, revoked, cancellationToken);
        var command = Command(Guid.NewGuid(), scenario.DestinationPartition,
            new AcceptQueueTransfer(scenario.DestinationQueue, intent.IntentToken));
        await AssertSdkFailureAsync(await sdk.CommitAsync(command, cancellationToken), ErrorCode.PermissionDenied);
        var message = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectAsync(
            new(scenario.DestinationQueue, TransferMessageId), cancellationToken));
        await Assert.That(message.Message).IsNull();
        var request = new InspectQueueTransferReceiptRequest(scenario.DestinationQueue, scenario.SourceQueue, transferId);
        var absent = await McpCallerAssertions.SuccessAsync<QueueTransferReceiptReadResult>(
            await mcp.CallAsync(InspectReceiptTool, request, cancellationToken));
        await Assert.That(absent.Value.Receipt).IsNull();
        return revoked;
    }

    private static async Task<QueueTransferReceiptInspection> ReadDestinationReceiptAsync(KeyLoadClient sdk,
        McpOfficialClient mcp, MessagingClusterScenario scenario, Guid transferId,
        string credential, CancellationToken cancellationToken)
    {
        var request = new InspectQueueTransferReceiptRequest(scenario.DestinationQueue, scenario.SourceQueue, transferId);
        var sdkResponse = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectQueueTransferReceiptAsync(request,
            cancellationToken));
        var sdkReceipt = sdkResponse.Receipt;
        var reply = await mcp.CallAsync(InspectReceiptTool, request, cancellationToken);
        var mcpReceipt = await McpCallerAssertions.SuccessAsync<QueueTransferReceiptReadResult>(reply);
        await Assert.That(sdkReceipt).IsNotNull();
        await Assert.That(sdkReceipt!.TransferId).IsEqualTo(transferId);
        await Assert.That(mcpReceipt.Value.Receipt).IsEqualTo(sdkReceipt);
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, credential, MessagingClusterScenario.ProtectedPayload);
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, credential, MessagingClusterScenario.ProtectedHeaders);
        return sdkReceipt;
    }

    private static async Task AcknowledgeTransferredMessageAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        MessagingClusterScenario scenario, CancellationToken cancellationToken)
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
        await Assert.That(JsonDefaults.Serialize(sdkAck).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(mcpAck.Value))).IsTrue();
        var persisted = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectAsync(
            new(scenario.DestinationQueue, TransferMessageId), cancellationToken));
        await Assert.That(persisted.Message).IsNotNull();
        await Assert.That(persisted.Message!.Metadata.State).IsEqualTo(MessageState.Acked);
    }

    private static async Task AcceptAfterAcknowledgementAsync(KeyLoadClient sdk, MessagingClusterScenario scenario,
        QueueTransferInspection intent, Guid transferId, QueueTransferReceiptInspection receipt,
        CancellationToken cancellationToken)
    {
        var accept = Command(Guid.NewGuid(), scenario.DestinationPartition,
            new AcceptQueueTransfer(scenario.DestinationQueue, intent.IntentToken));
        _ = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(accept, cancellationToken));
        var current = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectQueueTransferReceiptAsync(
            new(scenario.DestinationQueue, scenario.SourceQueue, transferId), cancellationToken));
        await Assert.That(current.Receipt).IsEqualTo(receipt);
        var persisted = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectAsync(
            new(scenario.DestinationQueue, TransferMessageId), cancellationToken));
        await Assert.That(persisted.Message).IsNotNull();
        await Assert.That(persisted.Message!.Metadata.State).IsEqualTo(MessageState.Acked);
        var next = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReceiveAsync(
            new(Guid.NewGuid(), scenario.DestinationQueue), cancellationToken));
        await Assert.That(next.Deliveries).IsEmpty();
    }

    private static async Task CompleteSourceAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        MessagingClusterScenario scenario, Guid transferId, QueueTransferReceiptInspection receipt,
        CancellationToken cancellationToken)
    {
        var complete = Command(Guid.NewGuid(), scenario.SourcePartition,
            new CompleteQueueTransfer(scenario.SourceQueue, transferId, receipt.ReceiptToken));
        _ = await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await mcp.CallAsync(McpCallerTools.DocumentsCommit, complete, cancellationToken));
        var request = new InspectQueueTransferRequest(scenario.SourceQueue, transferId);
        var finalResponse = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectQueueTransferAsync(request,
            cancellationToken));
        var final = finalResponse.Transfer;
        await Assert.That(final).IsNotNull();
        await Assert.That(final!.State).IsEqualTo(QueueTransferState.Delivered);
        await Assert.That(final.ReceiptToken).IsEqualTo(receipt.ReceiptToken);
        var mcpFinal = await McpCallerAssertions.SuccessAsync<QueueTransferReadResult>(
            await mcp.CallAsync(InspectSourceTool, request, cancellationToken));
        await Assert.That(mcpFinal.Value.Transfer).IsEqualTo(final);
    }

    private static async Task AssertSdkFailureAsync<T>(ManagedCode.Communication.Result<T> result,
        ErrorCode expected)
    {
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Problem?.ErrorCode).IsEqualTo(expected.ToString());
    }

    private static string UseGrant => string.Concat(MessagingClusterScenario.SensitiveGrant, ".use");
}
