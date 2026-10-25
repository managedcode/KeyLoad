
namespace KeyLoad.ProcessRecoveryTests.StorageRecovery;

internal static class KilledProcessFileReadiness
{
    private const string MetadataWalPath = "tree/0.meta.wal";
    private static readonly TimeSpan ReadinessTimeout = RecoveryExecutionOptions.ProcessReadiness().Value.OwnershipTimeout;
    private static readonly TimeSpan PollInterval = RecoveryExecutionOptions.ProcessReadiness().Value.OwnershipPollInterval;
    private static readonly string[] RequiredLockedFiles = ["owner.lock", "commands.wal"];

    internal static async Task WaitAsync(string root, CancellationToken cancellationToken)
    {
        var clock = TimeProvider.System;
        var started = clock.GetTimestamp();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                EnsureFilesUnlocked(root);
                return;
            }
            catch (IOException) when (clock.GetElapsedTime(started) < ReadinessTimeout)
            {
                await Task.Delay(PollInterval, clock, cancellationToken);
            }
        }
    }

    internal static void EnsureFilesUnlocked(string root)
    {
        foreach (var file in RequiredLockedFiles)
        {
            using (File.Open(Path.Combine(root, file), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
            }
        }

        try
        {
            using (File.Open(Path.Combine(root, MetadataWalPath), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
            }
        }
        catch (FileNotFoundException)
        {
            // Snapshot installation can move the tree before publishing its replacement.
        }
        catch (DirectoryNotFoundException)
        {
            // Snapshot installation can move the tree before publishing its replacement.
        }
    }
}
