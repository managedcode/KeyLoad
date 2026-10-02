using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.Orleans;

internal sealed class ReplicaMembershipStore
{
    private static readonly byte[] Key = KeyCodec.Encode(ReplicaMembershipProtocol.StorageSpace, ReplicaMembershipProtocol.TableKey);
    private readonly DatabaseEngine database;
    private readonly ICommitCoordinator coordinator;
    private readonly string principal;

    internal ReplicaMembershipStore(DatabaseEngine database, ICommitCoordinator coordinator, string principal)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentException.ThrowIfNullOrWhiteSpace(principal);
        this.database = database;
        this.coordinator = coordinator;
        this.principal = principal;
    }

    internal async Task<ReplicaMembershipSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        await coordinator.ReadBarrierAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var record = database.Store.Read(view => view.GetRecord<MembershipRecord>(Key));
        return ReplicaMembershipSnapshot.Read(record);
    }

    internal async Task<bool> CompareExchangeAsync(ReplicaMembershipSnapshot snapshot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var mutation = new MembershipMutation(ReplicaMembershipProtocol.TableKey, snapshot.ExpectedVersion, snapshot.Serialize());
        var result = await coordinator.SubmitAsync(OperationKind.Membership, Guid.NewGuid(), principal,
            JsonSerializer.Serialize(mutation, JsonDefaults.Options), cancellationToken).ConfigureAwait(false);
        return result.Get<bool>();
    }
}
