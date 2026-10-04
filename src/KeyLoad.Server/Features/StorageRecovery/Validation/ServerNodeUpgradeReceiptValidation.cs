using KeyLoad.Replication;

namespace KeyLoad.Server;

internal static class ServerNodeUpgradeReceiptValidation
{
    internal static void Verify(ServerNodeUpgradeOwner owner, ServerNodeUpgradeReceipt receipt,
        NodeOptions options)
    {
        if (receipt.FormatVersion != ServerNodeUpgradeProtocol.ReceiptFormatVersion
            || owner.FormatVersion != ServerNodeUpgradeProtocol.OwnerFormatVersion
            || receipt.SourceEpoch != owner.SourceEpoch
            || owner.SourceEpoch is not (ServerNodeUpgradeProtocol.Native5SourceEpoch
                or ServerNodeUpgradeProtocol.Native6SourceEpoch)
            || receipt.TargetEpoch != owner.TargetEpoch || owner.TargetEpoch != ServerNodeUpgradeProtocol.TargetEpoch
            || receipt.OriginalSource != owner.OriginalSource || receipt.FinalDestination != owner.FinalDestination
            || receipt.OriginalInventorySha256 != owner.OriginalInventorySha256
            || receipt.CanonicalSourceIdentitySha256 != owner.CanonicalIdentitySha256
            || receipt.CanonicalSourceJournalSha256 != owner.CanonicalJournalSha256
            || receipt.ReplicaSourceIdentitySha256 != owner.ReplicaIdentitySha256
            || receipt.ReplicaSourceJournalSha256 != owner.ReplicaJournalSha256
            || receipt.CanonicalNodeId == Guid.Empty || receipt.ReplicaNodeId == Guid.Empty
            || receipt.CanonicalNodeId == receipt.ReplicaNodeId || receipt.Incarnation != options.Incarnation
            || receipt.CanonicalPosition < 0 || receipt.ReplicaPhysicalPositionBefore < 0
            || receipt.CanonicalAppliedPosition < 0 || receipt.CanonicalReadGeneration < 0 || receipt.ReplicaReadGeneration < 0
            || receipt.SourceBackupDirectoryCount < 0 || !IsDigest(receipt.PreparedTargetInventorySha256))
        { throw Errors.Fail(ErrorCode.FormatUnsupported, ServerNodeUpgradeProtocol.Invalid); }
        VerifyOriginalState(receipt);
    }

    internal static bool IsDigest(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);

    private static void VerifyOriginalState(ServerNodeUpgradeReceipt receipt)
    {
        var state = receipt.OriginalReplicaHardState;
        if (state is null || state.Version != ReplicaProtocol.FormatVersion || state.Incarnation != receipt.Incarnation
            || state.Term < 0 || state.LastIndex < 0 || state.CommittedIndex < 0 || state.CommittedIndex > state.LastIndex
            || receipt.CanonicalAppliedPosition > state.CommittedIndex
            || receipt.CanonicalAppliedPosition < (state.Snapshot?.Index ?? 0))
        { throw Errors.Fail(ErrorCode.Corruption, ServerNodeUpgradeProtocol.Corrupt); }
    }
}
