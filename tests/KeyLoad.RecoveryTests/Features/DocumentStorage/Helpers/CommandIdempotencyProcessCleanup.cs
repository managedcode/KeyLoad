namespace KeyLoad.RecoveryTests.Features.DocumentStorage;

internal static class CommandIdempotencyProcessCleanup
{
    private const string UnsettledChildMessage = "The unsettled command child retained its native trial root.";

    internal static async Task CleanupActiveTrialAsync(string root, CommandIdempotencyProcessChild? active, Exception? primary)
    {
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(
            CommandIdempotencyProcessRecoveryTests.CleanupTimeoutSeconds));
        var failures = new List<Exception>();
        if (active is not null)
        { await CommandIdempotencyProcess.SettleActiveAsync(active, failures, cleanup.Token); }
        if (active is null || active.IsSettled)
        { await CommandIdempotencyProcess.CleanTrialAsync(root, failures, cleanup.Token); }
        else
        { failures.Add(new IOException(UnsettledChildMessage)); }
        CommandIdempotencyProcess.ThrowFailures(primary, failures);
    }
}
