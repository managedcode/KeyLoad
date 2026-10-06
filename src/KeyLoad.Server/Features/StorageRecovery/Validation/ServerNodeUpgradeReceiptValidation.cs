using KeyLoad.Replication;

namespace KeyLoad.Server;

internal static class ServerNodeUpgradeReceiptValidation
{
    private const int HasInvalidReceiptPositionsCanonicalPositionValidationBoundary = 0;
    private const int HasInvalidReceiptPositionsReplicaPhysicalPositionBeforeValidationBoundary = 0;
    private const int HasInvalidReceiptPositionsCanonicalAppliedPositionValidationBoundary = 0;
    private const int HasInvalidReceiptPositionsCanonicalReadGenerationValidationBoundary = 0;
    private const int HasInvalidReceiptPositionsReplicaReadGenerationValidationBoundary = 0;
    private const int HasInvalidReceiptPositionsSourceBackupDirectoryCountValidationBoundary = 0;
    private const int IsDigestValueValidationBound = 64;

    internal static void Verify(ServerNodeUpgradeOwner owner, ServerNodeUpgradeReceipt receipt,
        NodeOptions options)
    {
        if (HasInvalidHeader(owner, receipt)
            || HasInvalidSourceIdentity(owner, receipt)
            || HasInvalidReceiptPositions(receipt, options))
        { throw Errors.Fail(ErrorCode.FormatUnsupported, ServerNodeUpgradeProtocol.Invalid); }
        VerifyOriginalState(receipt);
    }

    private static bool HasInvalidHeader(ServerNodeUpgradeOwner owner, ServerNodeUpgradeReceipt receipt)
        => receipt.FormatVersion != ServerNodeUpgradeProtocol.ReceiptFormatVersion
            || owner.FormatVersion != ServerNodeUpgradeProtocol.OwnerFormatVersion
            || receipt.SourceEpoch != owner.SourceEpoch
            || owner.SourceEpoch is not (ServerNodeUpgradeProtocol.Native5SourceEpoch
                or ServerNodeUpgradeProtocol.Native6SourceEpoch)
            || receipt.TargetEpoch != owner.TargetEpoch || owner.TargetEpoch != ServerNodeUpgradeProtocol.TargetEpoch;

    private static bool HasInvalidSourceIdentity(ServerNodeUpgradeOwner owner, ServerNodeUpgradeReceipt receipt)
        => receipt.OriginalSource != owner.OriginalSource || receipt.FinalDestination != owner.FinalDestination
            || receipt.OriginalInventorySha256 != owner.OriginalInventorySha256
            || receipt.CanonicalSourceIdentitySha256 != owner.CanonicalIdentitySha256
            || receipt.CanonicalSourceJournalSha256 != owner.CanonicalJournalSha256
            || receipt.ReplicaSourceIdentitySha256 != owner.ReplicaIdentitySha256
            || receipt.ReplicaSourceJournalSha256 != owner.ReplicaJournalSha256;

    private static bool HasInvalidReceiptPositions(ServerNodeUpgradeReceipt receipt, NodeOptions options)
        => receipt.CanonicalNodeId == Guid.Empty || receipt.ReplicaNodeId == Guid.Empty
            || receipt.CanonicalNodeId == receipt.ReplicaNodeId || receipt.Incarnation != options.Incarnation
            || receipt.CanonicalPosition < HasInvalidReceiptPositionsCanonicalPositionValidationBoundary || receipt.ReplicaPhysicalPositionBefore < HasInvalidReceiptPositionsReplicaPhysicalPositionBeforeValidationBoundary
            || receipt.CanonicalAppliedPosition < HasInvalidReceiptPositionsCanonicalAppliedPositionValidationBoundary || receipt.CanonicalReadGeneration < HasInvalidReceiptPositionsCanonicalReadGenerationValidationBoundary || receipt.ReplicaReadGeneration < HasInvalidReceiptPositionsReplicaReadGenerationValidationBoundary
            || receipt.SourceBackupDirectoryCount < HasInvalidReceiptPositionsSourceBackupDirectoryCountValidationBoundary || !IsDigest(receipt.PreparedTargetInventorySha256);

    internal static bool IsDigest(string? value) => value is { Length: IsDigestValueValidationBound } && value.All(char.IsAsciiHexDigit);

    private static void VerifyOriginalState(ServerNodeUpgradeReceipt receipt)
    {
        const int TermValidationBoundary = 0;
        const int LastIndexValidationBoundary = 0;
        const int CommittedIndexValidationBoundary = 0;
        const int StateSnapshotIndexValidationBoundary = 0;

        var state = receipt.OriginalReplicaHardState;
        if (state is null || state.Version != ReplicaProtocol.FormatVersion || state.Incarnation != receipt.Incarnation
            || state.Term < TermValidationBoundary || state.LastIndex < LastIndexValidationBoundary || state.CommittedIndex < CommittedIndexValidationBoundary || state.CommittedIndex > state.LastIndex
            || receipt.CanonicalAppliedPosition > state.CommittedIndex
            || receipt.CanonicalAppliedPosition < (state.Snapshot?.Index ?? StateSnapshotIndexValidationBoundary))
        { throw Errors.Fail(ErrorCode.Corruption, ServerNodeUpgradeProtocol.Corrupt); }
    }
}
