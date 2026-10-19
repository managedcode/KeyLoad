using KeyLoad.Core;
using KeyLoad.Server;
using ManagedCode.Communication;
using Microsoft.Extensions.DependencyInjection;
using P = KeyLoad.UnitTests.Features.StorageRecovery.PartitionStorageProtocol;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class PartitionStorageHealthy
{
    internal static async Task<PartitionStorageCommit> ExecuteAsync(IServiceProvider source,
        CancellationToken token)
    {
        var owner = source.GetRequiredService<PartitionHostApplicationOwner>().Borrow();
        var definition = new ConfigureResourceRequest(P.Partition.TenantId, P.Partition.DatabaseId,
            new(P.Collection, ResourceKind.Collection, P.Partition.TransactionDomainId));
        var configured = await owner.Coordinator.RuntimeSubmitAsync(OperationKind.ConfigureResource,
            P.Configure, P.Root, OrleansSerialization.Serialize(definition), cancellationToken: token).ConfigureAwait(false);
        await Assert.That(Errors.Code(configured.Problem)).IsNull();
        await Assert.That(JsonDefaults.Serialize(configured.Get<ResourceDefinition>()).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(definition.Definition))).IsTrue();
        await owner.Coordinator.ReadBarrierAsync(token).ConfigureAwait(false);
        var before = PartitionStorageReplay.ReadCut(source);
        var reply = await owner.Coordinator.RuntimeSubmitAsync(OperationKind.Batch, P.Command, P.Root,
            OrleansSerialization.Serialize(P.Request()), cancellationToken: token).ConfigureAwait(false);
        var expected = new CommitReceipt(P.Command,
            new(owner.Database.Store.Identity.Incarnation, P.Partition.AtomicPartitionId,
                checked(before.AppliedPosition + P.CanonicalStep)),
            [new(P.PutKind, P.Collection, P.Document, P.InitialRevision)], DurabilityProfile.ProcessDurable, P.PersistedCopies);
        await RequireReceiptAsync(reply, expected).ConfigureAwait(false);
        await PartitionStorageRead.RequireAsync(source, expected.Token, token).ConfigureAwait(false);
        var bytes = PartitionStorageReplay.ReadBytes(source, token);
        var committed = new PartitionStorageCommit(expected, OrleansSerialization.Deserialize<StoredOutcome>(bytes), bytes);
        await PartitionStorageReplay.RequireOriginalAsync(source, committed, token).ConfigureAwait(false);
        return committed;
    }

    internal static async Task ReplayAsync(IServiceProvider source,
        PartitionStorageCommit original, CancellationToken token)
    {
        await PartitionStorageReplay.RequireOriginalAsync(source, original, token).ConfigureAwait(false);
        var before = PartitionStorageReplay.ReadCut(source);
        var owner = source.GetRequiredService<PartitionHostApplicationOwner>().Borrow();
        var reply = await owner.Coordinator.RuntimeSubmitAsync(OperationKind.Batch, P.Command, P.Root,
            OrleansSerialization.Serialize(P.Request()), cancellationToken: token).ConfigureAwait(false);
        await RequireReceiptAsync(reply, original.Receipt).ConfigureAwait(false);
        await PartitionStorageReplay.RequireOriginalAsync(source, original, token).ConfigureAwait(false);
        await PartitionStorageReplay.RequireUnchangedCutAsync(source, before, token).ConfigureAwait(false);
        await PartitionStorageRead.RequireAsync(source, original.Receipt.Token, token).ConfigureAwait(false);
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
