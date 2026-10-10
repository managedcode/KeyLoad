namespace KeyLoad.Core.Features.Messaging;

internal static class RemoteTransferDependencyShape
{
    internal static bool Valid(RemoteTransferAcceptDependency? value)
        => value is not null && value.PrincipalPolicyEpoch >= RemoteTransferAttemptProtocol.MinimumPolicyEpoch
            && Digest(value.FieldHeaderPolicyDigest) && value.QueueMaxStoredMessages > RemoteTransferAttemptProtocol.NoUsage
            && value.QueueMaxStoredBytes > RemoteTransferAttemptProtocol.NoUsage
            && value.QueueStoredMessages >= RemoteTransferAttemptProtocol.NoUsage && value.QueueStoredBytes >= RemoteTransferAttemptProtocol.NoUsage
            && value.TransferStoredRecords >= RemoteTransferAttemptProtocol.NoUsage && value.TransferStoredBytes >= RemoteTransferAttemptProtocol.NoUsage
            && value.MaxBatchBytes > RemoteTransferAttemptProtocol.NoUsage && value.MaxScanRecords > RemoteTransferAttemptProtocol.NoUsage;

    internal static bool Digest(string? value)
        => value is not null && value.Length == RemoteTransferAttemptProtocol.DigestCharacters && value.All(Uri.IsHexDigit);
}
