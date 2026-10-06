using KeyLoad.Core;
using KeyLoad.Replication;
using KeyLoad.Storage;

namespace KeyLoad.Server;

internal static class ServerNodeUpgradeCurrentState
{
    private const int HasInvalidCurrentProgressStateSnapshotIndexValidationBoundary = 0;

    internal static void VerifyPersisted(IAtomicStore replica)
    {
        if (replica.Read(view => view.ReadOwnedValue(KeyCodec.Encode(ReplicaProtocol.StateKey))) is null)
        { throw Errors.Fail(ErrorCode.Corruption, ServerNodeUpgradeProtocol.Corrupt); }
    }

    internal static void Verify(ServerNodeUpgradeReceipt receipt, DatabaseEngine canonical,
        IAtomicStore replica, ReplicaHardState state, ServerNodeUpgradeAuthority authority, bool published)
    {
        const int VerifyAbsentCount = 0;
        const int VerifyPresentCount = 1;

        VerifyIdentities(receipt, canonical.Store.Identity, replica.Identity, authority, published);
        var original = receipt.OriginalReplicaHardState;
        var minimumReplica = checked(receipt.ReplicaPhysicalPositionBefore + (original.Snapshot is null ? VerifyAbsentCount : VerifyPresentCount));
        if (HasInvalidCurrentProgress(receipt, canonical, replica, state, original, minimumReplica)
            || HasUnexpectedUnpublishedState(receipt, canonical, replica, state, original, minimumReplica, published))
        { throw Errors.Fail(ErrorCode.Corruption, ServerNodeUpgradeProtocol.Corrupt); }
    }

    private static bool HasInvalidCurrentProgress(ServerNodeUpgradeReceipt receipt, DatabaseEngine canonical,
        IAtomicStore replica, ReplicaHardState state, ReplicaHardState original, long minimumReplica)
        => canonical.Store.Position < receipt.CanonicalPosition || replica.Position < minimumReplica
            || canonical.LastApplied < receipt.CanonicalAppliedPosition || canonical.LastApplied > state.CommittedIndex
            || canonical.LastApplied < (state.Snapshot?.Index ?? HasInvalidCurrentProgressStateSnapshotIndexValidationBoundary) || state.Term < original.Term
            || state.CommittedIndex < original.CommittedIndex || state.LastIndex < original.LastIndex;

    private static bool HasUnexpectedUnpublishedState(ServerNodeUpgradeReceipt receipt, DatabaseEngine canonical,
        IAtomicStore replica, ReplicaHardState state, ReplicaHardState original, long minimumReplica, bool published)
        => !published && (canonical.Store.Position != receipt.CanonicalPosition || replica.Position != minimumReplica
            || canonical.LastApplied != receipt.CanonicalAppliedPosition || state.Term != original.Term
            || state.VotedFor != original.VotedFor || state.LastIndex != original.LastIndex
            || state.CommittedIndex != original.CommittedIndex || !SameSnapshotCut(state.Snapshot, original.Snapshot));

    private static void VerifyIdentities(ServerNodeUpgradeReceipt receipt, StoreIdentity canonical,
        StoreIdentity replica, ServerNodeUpgradeAuthority authority, bool published)
    {
        if (canonical.Durability != authority.Canonical.Durability || replica.Durability != authority.Replica.Durability
            || canonical.KeyCodecVersion != authority.Canonical.KeyCodecVersion || replica.KeyCodecVersion != authority.Replica.KeyCodecVersion
            || !published && (canonical.DispatchPaused != authority.Canonical.DispatchPaused
                || replica.DispatchPaused != authority.Replica.DispatchPaused)
            || canonical.FormatVersion != ServerNodeUpgradeProtocol.TargetEpoch || replica.FormatVersion != ServerNodeUpgradeProtocol.TargetEpoch
            || canonical.NodeId != receipt.CanonicalNodeId || replica.NodeId != receipt.ReplicaNodeId
            || canonical.Incarnation != receipt.Incarnation || replica.Incarnation != receipt.Incarnation
            || canonical.ReadGeneration < receipt.CanonicalReadGeneration || replica.ReadGeneration < receipt.ReplicaReadGeneration
            || !published && (canonical.ReadGeneration != receipt.CanonicalReadGeneration
                || replica.ReadGeneration != receipt.ReplicaReadGeneration))
        { throw Errors.Fail(ErrorCode.FormatUnsupported, ServerNodeUpgradeProtocol.Invalid); }
    }

    private static bool SameSnapshotCut(ReplicaSnapshot? current, ReplicaSnapshot? original)
        => original is null ? current is null : current is not null
            && current.TransferId == original.TransferId && current.Incarnation == original.Incarnation
            && current.Index == original.Index && current.Term == original.Term && current.FileName == original.FileName;
}
