using System.Diagnostics;

namespace KeyLoad.RecoveryTests.Features.StorageRecovery;

internal static class KilledProcessFileReadiness
{
    private const int ReadinessTimeoutSeconds = 5;
    private const int PollIntervalMilliseconds = 25;
    private static readonly TimeSpan ReadinessTimeout = TimeSpan.FromSeconds(ReadinessTimeoutSeconds);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(PollIntervalMilliseconds);
    private static readonly string[] LockedFiles = ["owner.lock", "commands.wal", Path.Combine("tree", "0.meta.wal")];

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

    private static void EnsureFilesUnlocked(string root)
    {
        foreach (var file in LockedFiles)
        {
            using (File.Open(Path.Combine(root, file), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
            }
        }
    }
}
