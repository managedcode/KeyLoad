using System.Diagnostics;

namespace KeyLoad.RecoveryTests.Features.StorageRecovery;

internal static class KilledProcessFileReadiness
{
    private const int ReadinessTimeoutSeconds = 5;
    private const int PollIntervalMilliseconds = 25;
    private const string MetadataWalPath = "tree/0.meta.wal";
    private static readonly TimeSpan ReadinessTimeout = TimeSpan.FromSeconds(ReadinessTimeoutSeconds);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(PollIntervalMilliseconds);
    private static readonly string[] RequiredLockedFiles = ["owner.lock", "commands.wal"];

    internal static async Task WaitAsync(string root, CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                EnsureFilesUnlocked(root);
                return;
            }
            catch (IOException) when (started.Elapsed < ReadinessTimeout)
            {
                await Task.Delay(PollInterval, cancellationToken);
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
