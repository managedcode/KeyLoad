namespace KeyLoad.Core.Features.Messaging;

internal static class RemoteTransferCapacityRepair
{
    internal static bool Improved(RemoteTransferAcceptFailureClaims claims)
    {
        var original = claims.OriginalAuthority.Dependency;
        var current = claims.CurrentDependency;
        if (current.PrincipalPolicyEpoch != original.PrincipalPolicyEpoch
            || current.FieldHeaderPolicyDigest != original.FieldHeaderPolicyDigest)
        { return false; }
        return claims.FailureKind switch
        {
            RemoteTransferAcceptFailureKind.QueueStorage => current.QueueStoredMessages < original.QueueStoredMessages
                || current.QueueStoredBytes < original.QueueStoredBytes
                || current.QueueMaxStoredMessages > original.QueueMaxStoredMessages
                || current.QueueMaxStoredBytes > original.QueueMaxStoredBytes,
            RemoteTransferAcceptFailureKind.TransferRetention => current.TransferStoredRecords < original.TransferStoredRecords
                || current.TransferStoredBytes < original.TransferStoredBytes
                || current.MaxBatchBytes > original.MaxBatchBytes || current.MaxScanRecords > original.MaxScanRecords,
            _ => false
        };
    }
}
