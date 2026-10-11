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
        var originalImage = ClusterRestoreRf3FaultImage.Capture(target);
        var changed = original.ToArray();
        changed[MagicByte] ^= FlipMagicBit;
        var failures = new List<Exception>();
        var faultWritten = false;
        var joined = false;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            Replace(path, original, changed);
            faultWritten = true;
            var corruptImage = ClusterRestoreRf3FaultImage.Capture(target);
            await target.StartRejectedCorruptPlanAsync(published, cancellationToken).ConfigureAwait(false);
            await ClusterRestoreRf3FaultImageAssertions.RequireAsync(target, corruptImage).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await target.StopAsync().ConfigureAwait(false);
            joined = true;
        }, failures).ConfigureAwait(false);
        if (faultWritten && joined)
        { ServerFailureObserver.Observe(() => Replace(path, changed, original), failures); }
        ServerFailureObserver.ThrowIfAny(failures);
        await ClusterRestoreRf3FaultImageAssertions.RequireAsync(target, originalImage).ConfigureAwait(false);
    }

    private static void Replace(string path, byte[] expected, byte[] replacement)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != default
            || expected.Length != replacement.Length)
        { throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid); }
        using var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        if (file.Length != expected.Length)
        { throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid); }
        var actual = new byte[expected.Length];
        file.ReadExactly(actual);
        if (!actual.AsSpan().SequenceEqual(expected))
        { throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid); }
        file.Position = MagicByte;
        file.Write(replacement);
        file.Flush(flushToDisk: true);
    }
}
