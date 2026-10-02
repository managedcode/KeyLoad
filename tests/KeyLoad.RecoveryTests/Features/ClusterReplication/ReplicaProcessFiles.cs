using KeyLoad.CrashHost;

namespace KeyLoad.RecoveryTests;

internal static class ReplicaProcessFiles
{
    internal const string Journal = "commands.wal";
    private const string Ownership = "owner.lock";
    private const string Solution = "KeyLoad.slnx";
    private const string Artifacts = "artifacts";
    private const string Qualification = "qualification";
    private const string EvidencePrefix = "replica-process-";
    private const string EvidenceExtension = ".json";
    private const string GuidFormat = "N";
    private static readonly TimeSpan RetryWindow = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(25);

    internal static async Task WaitForOwnershipAsync(string root, CancellationToken cancellationToken)
    {
        var started = TimeProvider.System.GetTimestamp();
        while (true)
        {
            try
            {
                EnsureTargetFilesReleased(root);
                return;
            }
            catch (IOException) when (TimeProvider.System.GetElapsedTime(started) < RetryWindow)
            {
                await Task.Delay(RetryDelay, TimeProvider.System, cancellationToken);
            }
        }
    }

    private static void EnsureTargetFilesReleased(string root)
    {
        foreach (var directory in ReplicaCrashNode.TargetStoreDirectories(root))
        {
            EnsureOwnershipFilesReleased(directory);
        }
    }

    private static void EnsureOwnershipFilesReleased(string directory)
    {
        foreach (var name in new[] { Ownership, Journal })
        {
            using var released = File.Open(Path.Combine(directory, name), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
    }

    internal static async Task DeleteAsync(string root, CancellationToken cancellationToken)
    {
        var started = TimeProvider.System.GetTimestamp();
        while (Directory.Exists(root))
        {
            try
            { Directory.Delete(root, true); }
            catch (IOException) when (TimeProvider.System.GetElapsedTime(started) < RetryWindow)
            {
                await Task.Delay(RetryDelay, TimeProvider.System, cancellationToken);
            }
        }
    }

    internal static void WriteEvidence(ReplicaCrashReady ready, int processId, int exitCode)
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository.Parent is not null && !File.Exists(Path.Combine(repository.FullName, Solution)))
        {
            repository = repository.Parent;
        }
        var directory = Path.Combine(File.Exists(Path.Combine(repository.FullName, Solution))
            ? repository.FullName : AppContext.BaseDirectory, Artifacts, Qualification);
        Directory.CreateDirectory(directory);
        var name = EvidencePrefix + ready.Boundary + Guid.NewGuid().ToString(GuidFormat) + EvidenceExtension;
        File.WriteAllBytes(Path.Combine(directory, name), JsonDefaults.Serialize(new
        {
            Ready = ready,
            ProcessId = processId,
            ExitCode = exitCode,
            KilledEntireProcessTree = true,
            ObservedAt = TimeProvider.System.GetUtcNow()
        }));
    }
}
