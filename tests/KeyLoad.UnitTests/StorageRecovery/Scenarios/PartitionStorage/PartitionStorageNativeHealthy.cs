using KeyLoad.Core;
using KeyLoad.UnitTests.Features.ClusterRouting;
using ManagedCode.Communication;
using P = KeyLoad.UnitTests.Features.StorageRecovery.PartitionStorageNativeProtocol;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class PartitionStorageNativeHealthy
{
    internal static async Task<PartitionStorageNativeCommit> ExecuteAsync(RequestCqrsHostedRuntimeFixture source,
        CancellationToken token)
    {
        var definition = new ConfigureResourceRequest(P.Partition.TenantId, P.Partition.DatabaseId,
            new(P.Collection, ResourceKind.Collection, P.Partition.TransactionDomainId));
        var configured = await source.Database.Coordinator.SubmitNativeAsync(OperationKind.ConfigureResource,
            P.Configure, P.Root, NativeSerialization.Serialize(definition), cancellationToken: token).ConfigureAwait(false);
        await Assert.That(Errors.Code(configured.Problem)).IsNull();
        await Assert.That(JsonDefaults.Serialize(configured.Get<ResourceDefinition>()).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(definition.Definition))).IsTrue();
        await source.Database.Coordinator.ReadBarrierAsync(token).ConfigureAwait(false);
        var before = source.Database.ReadCanonicalCut();
        var reply = await source.Database.Coordinator.SubmitNativeAsync(OperationKind.Batch, P.Command, P.Root,
            NativeSerialization.Serialize(P.Request()), cancellationToken: token).ConfigureAwait(false);
        var expected = new CommitReceipt(P.Command,
            new(source.Database.Store.Identity.Incarnation, P.Partition.AtomicPartitionId,
                checked(before.AppliedPosition + P.CanonicalStep)),
            [new(P.PutKind, P.Collection, P.Document, P.InitialRevision)], DurabilityProfile.ProcessDurable, P.PersistedCopies);
        await RequireReceiptAsync(reply, expected).ConfigureAwait(false);
        await PartitionStorageNativeRead.RequireAsync(source, expected.Token, token).ConfigureAwait(false);
        var bytes = PartitionStorageNativeReplay.ReadBytes(source, token);
        var committed = new PartitionStorageNativeCommit(expected, NativeSerialization.Deserialize<StoredOutcome>(bytes), bytes);
        await PartitionStorageNativeReplay.RequireOriginalAsync(source, committed, token).ConfigureAwait(false);
        return committed;
    }

    internal static async Task ReplayAsync(RequestCqrsHostedRuntimeFixture source,
        PartitionStorageNativeCommit original, CancellationToken token)
    {
        await PartitionStorageNativeReplay.RequireOriginalAsync(source, original, token).ConfigureAwait(false);
        var before = source.Database.ReadCanonicalCut();
        var reply = await source.Database.Coordinator.SubmitNativeAsync(OperationKind.Batch, P.Command, P.Root,
            NativeSerialization.Serialize(P.Request()), cancellationToken: token).ConfigureAwait(false);
        await RequireReceiptAsync(reply, original.Receipt).ConfigureAwait(false);
        await PartitionStorageNativeReplay.RequireOriginalAsync(source, original, token).ConfigureAwait(false);
        await PartitionStorageNativeReplay.RequireUnchangedCutAsync(source, before, token).ConfigureAwait(false);
        await PartitionStorageNativeRead.RequireAsync(source, original.Receipt.Token, token).ConfigureAwait(false);
    }

    private static async Task RequireReceiptAsync(Result<object> reply, CommitReceipt expected)
    {
        await Assert.That(Errors.Code(reply.Problem)).IsNull();
        await Assert.That(reply.Problem).IsNull();
        var actual = reply.Get<CommitReceipt>();
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected)))
            .IsTrue();
        await Assert.That(actual.Mutations.Single().CompositionReferences.IsEmpty).IsTrue();
    }
}
