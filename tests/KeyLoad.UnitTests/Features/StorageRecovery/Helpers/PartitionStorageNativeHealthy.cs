using KeyLoad.Clustering;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.UnitTests.Features.ClusterRouting;
using P = KeyLoad.UnitTests.Features.StorageRecovery.PartitionStorageNativeProtocol;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class PartitionStorageNativeHealthy
{
    internal static async Task<CommitReceipt> ExecuteAsync(ControlledPartitionMovementNativeHost source,
        CancellationToken token)
    {
        var definition = new ConfigureResourceRequest(P.Partition.TenantId, P.Partition.DatabaseId,
            new(P.Collection, ResourceKind.Collection, P.Partition.TransactionDomainId));
        var configured = await source.SubmitAsync(OperationKind.ConfigureResource, P.Configure, P.Root,
            NativeSerialization.Serialize(definition), token).ConfigureAwait(false);
        await Assert.That(configured.Error).IsNull();
        await Assert.That(configured.SafeDetail).IsNull();
        var reply = await source.SubmitAsync(OperationKind.Batch, P.Command, P.Root,
            NativeSerialization.Serialize(P.Request()), token).ConfigureAwait(false);
        var entry = await ControlledPartitionMovementNativeEntryReader.ReadAsync(source, P.Command, token)
            .ConfigureAwait(false);
        await Assert.That(entry.Operation is not null).IsTrue();
        await Assert.That(entry.Operation!.Id).IsEqualTo(P.Command);
        await Assert.That(entry.Operation.Kind).IsEqualTo(OperationKind.Batch);
        await Assert.That(entry.Operation.PrincipalId).IsEqualTo(P.Root);
        var expected = new CommitReceipt(P.Command,
            new(source.Store.Identity.Incarnation, P.Partition.AtomicPartitionId, entry.Index, P.OwnershipEpoch),
            [new(P.PutKind, P.Collection, P.Document, P.InitialRevision)], DurabilityProfile.QuorumProcessDurable);
        await RequireReceiptAsync(reply, expected).ConfigureAwait(false);
        await PartitionStorageNativeRead.RequireAsync(source, expected.Token, token).ConfigureAwait(false);
        return expected;
    }

    internal static async Task ReplayAsync(ControlledPartitionMovementNativeHost source,
        CommitReceipt expected, CancellationToken token)
    {
        var reply = await source.SubmitAsync(OperationKind.Batch, P.Command, P.Root,
            NativeSerialization.Serialize(P.Request()), token).ConfigureAwait(false);
        await RequireReceiptAsync(reply, expected).ConfigureAwait(false);
        var entry = await ControlledPartitionMovementNativeEntryReader.ReadAsync(source, P.Command, token)
            .ConfigureAwait(false);
        await Assert.That(entry.Index).IsEqualTo(expected.Token.Position);
        await PartitionStorageNativeRead.RequireAsync(source, expected.Token, token).ConfigureAwait(false);
    }

    private static async Task RequireReceiptAsync(GrainOperationReply reply, CommitReceipt expected)
    {
        await Assert.That(reply.Error).IsNull();
        await Assert.That(reply.SafeDetail).IsNull();
        var actual = GrainNativePayload.Read<GrainValue>(reply.Payload).Value as CommitReceipt;
        await Assert.That(actual is not null).IsTrue();
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected)))
            .IsTrue();
        await Assert.That(actual!.Mutations.Single().CompositionReferences.IsEmpty).IsTrue();
    }
}
