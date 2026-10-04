using KeyLoad.Core;
using KeyLoad.Replication;
using KeyLoad.Storage;

namespace KeyLoad.Orleans;

internal sealed class ReplicaMembershipStore
{
    private static readonly byte[] Key = KeyCodec.Encode(ReplicaMembershipProtocol.StorageSpace, ReplicaMembershipProtocol.TableKey);
    private readonly DatabaseEngine database;
    private readonly ICommitCoordinator coordinator;
    private readonly ReplicaConsensus consensus;
    private readonly string principal;

    internal ReplicaMembershipStore(DatabaseEngine database, ICommitCoordinator coordinator, ReplicaConsensus consensus, string principal)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(consensus);
        ArgumentException.ThrowIfNullOrWhiteSpace(principal);
        this.database = database;
        this.coordinator = coordinator;
        this.consensus = consensus;
        this.principal = principal;
    }

    internal async Task<ReplicaMembershipSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await consensus.ReadControlBarrierAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var record = database.Store.Read(view => view.GetRecord<MembershipRecord>(Key));
        return ReplicaMembershipSnapshot.Read(record);
    }

    internal async Task<bool> CompareExchangeAsync(ReplicaMembershipSnapshot snapshot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var mutation = new MembershipMutation(ReplicaMembershipProtocol.TableKey, snapshot.ExpectedVersion, snapshot.Serialize());
        var result = await coordinator.SubmitNativeAsync(OperationKind.Membership, Guid.NewGuid(), principal,
            NativeSerialization.Serialize(mutation), cancellationToken).ConfigureAwait(false);
        return result.Get<bool>();
    }
}
