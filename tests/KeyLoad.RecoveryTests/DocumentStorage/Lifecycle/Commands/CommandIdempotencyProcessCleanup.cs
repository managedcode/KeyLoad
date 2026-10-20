namespace KeyLoad.RecoveryTests.Features.DocumentStorage;

internal static class CommandIdempotencyProcessCleanup
{
    private const int NoCleanupFailures = 0;
    private const string UnsettledChildMessage = "The unsettled command child retained its execution trial root.";

    internal static async Task CleanupActiveTrialAsync(string root, CommandIdempotencyProcessChild? active, Exception? primary)
    {
        using var cleanup = new CancellationTokenSource(RecoveryExecutionOptions.TrialPolicy.Value.CleanupTimeout, TimeProvider.System);
        var failures = new List<Exception>();
        if (active is not null)
        {
            await CommandIdempotencyProcess.SettleActiveAsync(active, failures, cleanup.Token);
        }
        var childClosed = active is null || active.IsSettled && active.IsDisposed;
        if (!childClosed)
        {
            failures.Add(new IOException(UnsettledChildMessage));
        }
        if (primary is null && childClosed && failures.Count == NoCleanupFailures)
        {
            await CommandIdempotencyProcess.CleanTrialAsync(root, failures, cleanup.Token);
        }
        CommandIdempotencyProcess.ThrowFailures(primary, failures);
    }
}
