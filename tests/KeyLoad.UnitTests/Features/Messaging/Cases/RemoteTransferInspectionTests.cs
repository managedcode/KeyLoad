using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class RemoteTransferInspectionTests
{
    private const string InspectorWithoutUse = "inspector-without-use";
    private const string InspectorWithoutQueueGrant = "inspector-without-queue-grant";
    private const string InspectorWithUse = "inspector-with-use";
    private const string ProtectedPath = "secret";
    private const string Classification = "restricted";
    private const string RawUse = "pii.use";

    [Test]
    public async Task SourceAndDestinationProofReadsRequirePersistedQueueAndFieldUseAuthority()
    {
        var protectedPolicy = new[] { new SensitiveFieldPolicy(ProtectedPath, Classification) };
        using var fixture = new RemoteTransferDatabase(sourceFields: protectedPolicy, sourceHeaders: protectedPolicy,
            destinationFields: protectedPolicy, destinationHeaders: protectedPolicy);
        var transferId = Guid.NewGuid();
        fixture.Commit(fixture.SourcePartition, new CreateQueueTransfer(fixture.SourceQueue, transferId,
            fixture.DestinationQueue, new(fixture.DestinationQueue.Queue, "protected-message", "{\"secret\":\"x\"}",
                "{\"secret\":\"y\"}")));
        var intent = fixture.Database.InspectQueueTransfer(RemoteTransferDatabase.RootPrincipal,
            fixture.SourceQueue, transferId)!;

        fixture.AddPrincipal(Inspector(InspectorWithoutQueueGrant, [], []));
        fixture.AddPrincipal(Inspector(InspectorWithoutUse, [Capability.QueueInspect], []));
        var noQueueGrant = Assert.ThrowsExactly<KeyLoadException>(() =>
            fixture.Database.InspectQueueTransfer(InspectorWithoutQueueGrant, fixture.SourceQueue, transferId));
        var noFieldUse = Assert.ThrowsExactly<KeyLoadException>(() =>
            fixture.Database.InspectQueueTransfer(InspectorWithoutUse, fixture.SourceQueue, transferId));
        await Assert.That(noQueueGrant.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(noFieldUse.Code).IsEqualTo(ErrorCode.PermissionDenied);

        fixture.AddPrincipal(Inspector(InspectorWithUse,
            [Capability.QueueInspect], [RawUse]));
        var visibleIntent = fixture.Database.InspectQueueTransfer(InspectorWithUse, fixture.SourceQueue, transferId)!;
        await Assert.That(visibleIntent.IntentToken).IsEqualTo(intent.IntentToken);
        fixture.Commit(fixture.DestinationPartition,
            new AcceptQueueTransfer(fixture.DestinationQueue, intent.IntentToken));
        var visibleReceipt = fixture.Database.InspectQueueTransferReceipt(InspectorWithUse,
            fixture.DestinationQueue, fixture.SourceQueue, transferId);
        await Assert.That(visibleReceipt).IsNotNull();
        await Assert.That(visibleReceipt!.ReceiptToken)
            .IsEqualTo(fixture.Database.InspectQueueTransferReceipt(RemoteTransferDatabase.RootPrincipal,
                fixture.DestinationQueue, fixture.SourceQueue, transferId)!.ReceiptToken);
    }

    [Test]
    public async Task CancellationStopsBothProofReadsAndAHealthyReadStillWorks()
    {
        using var fixture = new RemoteTransferDatabase();
        var transferId = Guid.NewGuid();
        fixture.Commit(fixture.SourcePartition, new CreateQueueTransfer(fixture.SourceQueue, transferId,
            fixture.DestinationQueue, new(fixture.DestinationQueue.Queue, "cancelled-read", "{}")));
        var intent = fixture.Database.InspectQueueTransfer(RemoteTransferDatabase.RootPrincipal,
            fixture.SourceQueue, transferId)!;
        fixture.Commit(fixture.DestinationPartition,
            new AcceptQueueTransfer(fixture.DestinationQueue, intent.IntentToken));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        Assert.ThrowsExactly<OperationCanceledException>(() => fixture.Database.InspectQueueTransfer(
            RemoteTransferDatabase.RootPrincipal, fixture.SourceQueue, transferId, cancellation.Token));
        Assert.ThrowsExactly<OperationCanceledException>(() => fixture.Database.InspectQueueTransferReceipt(
            RemoteTransferDatabase.RootPrincipal, fixture.DestinationQueue, fixture.SourceQueue,
            transferId, cancellation.Token));
        await Assert.That(fixture.Database.InspectQueueTransfer(RemoteTransferDatabase.RootPrincipal,
            fixture.SourceQueue, transferId)!.State).IsEqualTo(QueueTransferState.OutputPending);
        await Assert.That(fixture.Database.InspectQueueTransferReceipt(RemoteTransferDatabase.RootPrincipal,
            fixture.DestinationQueue, fixture.SourceQueue, transferId)).IsNotNull();
    }

    private static PrincipalRecord Inspector(string id, Capability[] capabilities, string[] fields)
    {
        var grants = capabilities.Select(capability => new ScopeGrant(RemoteTransferDatabase.DatabaseId,
            RemoteTransferDatabase.SourceQueueName, capability)).ToList();
        if (capabilities.Contains(Capability.QueueInspect))
        {
            grants.Add(new(RemoteTransferDatabase.DatabaseId, RemoteTransferDatabase.DestinationQueueName,
                Capability.QueueInspect));
        }
        return new(id, RemoteTransferDatabase.TenantId, [.. grants], [.. fields]) { ClusterAdministrator = true };
    }
}
