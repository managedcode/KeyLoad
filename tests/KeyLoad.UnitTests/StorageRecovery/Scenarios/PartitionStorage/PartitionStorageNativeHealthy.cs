using KeyLoad.Clustering;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.UnitTests.Features.ClusterRouting;
using P = KeyLoad.UnitTests.Features.StorageRecovery.PartitionStorageNativeProtocol;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class PartitionStorageNativeHealthy
{
    internal static async Task<PartitionStorageNativeCommit> ExecuteAsync(ControlledPartitionMovementNativeHost source,
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
        await Assert.That(entry.Operation!.CommandId).IsEqualTo(P.Command);
        await Assert.That(entry.Operation.Value!.Kind).IsEqualTo(OperationKind.Batch);
        await Assert.That(entry.Operation.UserId!).IsEqualTo(P.Root);
        var expected = new CommitReceipt(P.Command, new(source.Store.Identity.Incarnation, P.Partition.AtomicPartitionId, entry.Index), [new(P.PutKind, P.Collection, P.Document, P.InitialRevision)], DurabilityProfile.ProcessDurable, 1);
        await RequireReceiptAsync(reply, expected).ConfigureAwait(false);
        await PartitionStorageNativeRead.RequireAsync(source, expected.Token, token).ConfigureAwait(false);
        return new(expected, entry, PartitionStorageNativeReplay.ReadBytes(source, entry.Index, token));
    }

    internal static async Task ReplayAsync(ControlledPartitionMovementNativeHost source,
        PartitionStorageNativeCommit original, CancellationToken token)
    {
        await PartitionStorageNativeReplay.RequireOriginalAsync(source, original, token).ConfigureAwait(false);
        var committedBefore = source.Log.State.CommittedIndex;
        var reply = await source.SubmitAsync(OperationKind.Batch, P.Command, P.Root,
            NativeSerialization.Serialize(P.Request()), token).ConfigureAwait(false);
        await RequireReceiptAsync(reply, original.Receipt).ConfigureAwait(false);
        await PartitionStorageNativeReplay.RequireOriginalAsync(source, original, token).ConfigureAwait(false);
        await PartitionStorageNativeReplay.RequireAdmissionAsync(source, committedBefore, token).ConfigureAwait(false);
        await PartitionStorageNativeRead.RequireAsync(source, original.Receipt.Token, token).ConfigureAwait(false);
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
