using System.Collections.Immutable;
using KeyLoad.Clustering;
using KeyLoad.Core;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Server;

/// <summary>Joins primary and replica publications to the original physical session's work ledger.</summary>
internal sealed class PartitionStorageBatchExecution(DatabaseEngine database, RequestWorkOwner owner, IGrainFactory grains)
{
    internal Task<SiloStorageCut> ReadCutAsync(PartitionRef? partition, CancellationToken token)
        => RunAsync(Guid.NewGuid(), cancellation =>
        {
            cancellation.ThrowIfCancellationRequested();
            return Task.FromResult(database.Store.Read(view => new SiloStorageCut(database.Store.Identity.Incarnation,
                StorageOperationApply.ReadCanonicalSequence(view, partition))));
        }, token);

    internal Task<StorageCopyBatchAcknowledgement> CommitAsync(Guid id,
        ImmutableArray<Command<DatabaseCommand>> operations, long firstSequence, bool retainReplication,
        CancellationToken token)
        => RunAsync(id, async cancellation =>
        {
            var prepared = database.PrepareStorageOperations(operations, cancellation);
            var result = await database.ApplyStorageOperationsAsync(prepared, firstSequence, cancellation, retainReplication);
            return StorageCopyBatchAcknowledgements.Create(result);
        }, token);

    internal Task<StorageCopyBatchAcknowledgement> CopyAsync(Guid id, StorageReplicaBatch batch, CancellationToken token)
        => RunAsync(id, async cancellation =>
        {
            try
            {
                return await database.ApplyReplicaBatchAsync(batch, cancellation);
            }
            catch (KeyLoadException error) when (error.Code == ErrorCode.RecoveryRequired
                && error.Message == StorageReplicaDependencies.NotReady)
            {
                var missing = StorageReplicaDependencies.Missing(database, batch, cancellation);
                await Task.WhenAll(missing.Select(cut => WaitForDependencyAsync(batch.Incarnation, cut, cancellation)));
                return await database.ApplyReplicaBatchAsync(batch, cancellation);
            }
        }, token);

    private Task WaitForDependencyAsync(Guid incarnation, StoragePartitionPosition cut, CancellationToken token)
        => cut.Partition is { } partition
            ? grains.GetGrain<IPartitionCommitGrain>(PartitionCommitGrainIdentity.For(incarnation, partition))
                .WaitForCopiedAsync(incarnation, partition, cut.Position, token)
            : grains.GetGrain<IDatabaseCommitGrain>(DatabaseGrainIdentity.For(incarnation))
                .WaitForCopiedAsync(cut.Position, token);

    private async Task<T> RunAsync<T>(Guid id, Func<CancellationToken, Task<T>> execute, CancellationToken token)
    {
        var work = CapabilityWorkLifetime.Acquire(owner, id, RequestWorkKind.RuntimeTransport, token).Value
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        await using (work)
        {
            try
            {
                return await execute(work.Token);
            }
            catch (Exception failure)
            {
                work.PrimaryError = CapabilityWorkLifetime.CombinePrimary(work.PrimaryError, failure);
                throw;
            }
        }
    }
}
