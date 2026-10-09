namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeEncodedFrameRejection
{
    internal static int DetailBytes
        => System.Text.Encoding.UTF8.GetByteCount(ZoneTreePersistenceFormat.EncodedTransactionFrameLimitExceeded);

    internal static bool Matches(OperationResult? result)
        => result is { Error: ErrorCode.ResourceExhausted }
            && string.Equals(result.SafeDetail, ZoneTreePersistenceFormat.EncodedTransactionFrameLimitExceeded,
                StringComparison.Ordinal);
}
