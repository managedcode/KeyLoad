using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

/// <summary>Changes only a stopped fixture's original framing byte, then restores its exact native bytes.</summary>
internal static class ClusterRestoreRf3PlanCorruption
{
    private const int MagicByte = 0;
    private const byte FlipMagicBit = 1;

    internal static async Task RequireAsync(ClusterRestoreRf3Fixture target, bool published,
        CancellationToken cancellationToken)
        => await RequireAsync(target, published, ClusterRestoreRf3ResumeProtocol.PlanFile, cancellationToken).ConfigureAwait(false);

    internal static async Task RequireAsync(ClusterRestoreRf3Fixture target, bool published, string stateFile,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(ClusterRestoreRf3RetainedCut.OperationRoot(target), stateFile);
        var file = new FileInfo(path);
        if (!file.Exists || file.Length <= MagicByte || file.Length > IntegrationExecutionOptions.StorageExecution().Value.MaximumBackupManifestBytes
            || (file.Attributes & FileAttributes.ReparsePoint) != default)
        { throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid); }
        var original = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var changed = original.ToArray();
            changed[MagicByte] ^= FlipMagicBit;
            Write(path, changed);
            var corrupt = ClusterRestoreRf3RetainedCut.Operation(target);
            await target.StartRejectedCorruptPlanAsync(published, cancellationToken).ConfigureAwait(false);
            await Assert.That(ClusterRestoreRf3RetainedCut.Operation(target).SequenceEqual(corrupt)).IsTrue();
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.Observe(() => Write(path, original), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static void Write(string path, byte[] bytes)
    {
        using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        file.Write(bytes);
        file.Flush(flushToDisk: true);
    }
}
