using KeyLoad.RecoveryTests.Features.StorageRecovery;
using Microsoft.Extensions.Options;

namespace KeyLoad.RecoveryTests.Features.ClusterRouting;

/// <summary>Checks both exact original native stores after the child and its readers have joined.</summary>
internal static class ControlledPartitionMovementProcessOwnership
{
    private const string Canonical = "database";
    private const string Replica = "replica";
    private const string Missing = "The original movement native store directory is absent.";

    internal static async Task RequireAsync(string root, IOptions<NativeProcessReadinessOptions> readiness,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        var started = clock.GetTimestamp();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                RequireRoots(root);
                return;
            }
            catch (IOException) when (clock.GetElapsedTime(started) < readiness.Value.OwnershipTimeout)
            { await Task.Delay(readiness.Value.OwnershipPollInterval, clock, cancellationToken); }
        }
    }

    private static void RequireRoots(string root)
    {
        foreach (var name in new[] { Canonical, Replica })
        {
            var directory = Path.Combine(root, name);
            if (!Directory.Exists(directory))
            { throw new DirectoryNotFoundException(Missing); }
            KilledProcessFileReadiness.EnsureFilesUnlocked(directory);
        }
    }
}
