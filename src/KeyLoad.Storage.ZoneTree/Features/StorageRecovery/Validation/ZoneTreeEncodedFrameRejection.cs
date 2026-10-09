namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeEncodedFrameRejection
{
    internal static bool Matches(OperationResult? result)
        => result is { Error: ErrorCode.ResourceExhausted }
            && string.Equals(result.SafeDetail, ZoneTreePersistenceFormat.EncodedTransactionFrameLimitExceeded,
                StringComparison.Ordinal);
}
