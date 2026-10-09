namespace KeyLoad.Server;

/// <summary>Full shutdown joins original page/session ownership before releasing its native work admission.</summary>
internal static class PartitionMovementSourceEntryDisposal
{
    internal static async Task<List<Exception>> ObserveSessionAsync(PartitionMovementImageSession session)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => session.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        return failures;
    }
}
