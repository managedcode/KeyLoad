using KeyLoad.Core;
using KeyLoad.Replication;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Orleans;

internal sealed class ReplicaMembershipStore
{
    private static readonly byte[] Key = KeyCodec.Encode(ReplicaMembershipProtocol.StorageSpace, ReplicaMembershipProtocol.TableKey);
    private readonly DatabaseEngine database;
    private readonly ICommitCoordinator coordinator;
    private readonly ReplicaConsensus consensus;
    private readonly string principal;
    private readonly int maximumRows;
    private readonly IOptions<OrleansMembershipOptions> membershipOptions;

    internal ReplicaMembershipStore(DatabaseEngine database, ICommitCoordinator coordinator, ReplicaConsensus consensus,
        string principal, IOptions<OrleansMembershipOptions> membershipOptions, int maximumRows = ReplicaMembershipProtocol.UnboundedRows)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(consensus);
        ArgumentException.ThrowIfNullOrWhiteSpace(principal);
        this.database = database;
        this.coordinator = coordinator;
        this.consensus = consensus;
        this.principal = principal;
        ArgumentNullException.ThrowIfNull(membershipOptions);
        membershipOptions.Value.Validate();
        this.membershipOptions = membershipOptions;
        this.maximumRows = maximumRows;
    }

    internal async Task<ReplicaMembershipSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        const int MaximumRowsValidationBoundary = 0;

        cancellationToken.ThrowIfCancellationRequested();
        await consensus.ReadControlBarrierAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var record = database.Store.Read(view => view.GetRecord<MembershipRecord>(Key));
        if (maximumRows > MaximumRowsValidationBoundary && record is not null
            && record.Payload.Length > membershipOptions.Value.MaximumSnapshotBytes)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, ReplicaMembershipAuthorityText.MembershipCapacity); }
        return ReplicaMembershipSnapshot.Read(record: record, maximumRows: maximumRows, membershipOptions: membershipOptions);
    }

    internal async Task<bool> CompareExchangeAsync(ReplicaMembershipSnapshot snapshot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        snapshot.ValidateCapacity(maximumRows);
        var mutation = new MembershipMutation(ReplicaMembershipProtocol.TableKey, snapshot.ExpectedVersion, snapshot.Serialize());
        var result = await coordinator.SubmitNativeAsync(OperationKind.Membership, Guid.NewGuid(), principal,
            NativeSerialization.Serialize(mutation), cancellationToken).ConfigureAwait(false);
        return result.Get<bool>();
    }
}
