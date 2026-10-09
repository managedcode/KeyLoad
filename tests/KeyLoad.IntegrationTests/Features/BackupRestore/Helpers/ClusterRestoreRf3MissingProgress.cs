namespace KeyLoad.IntegrationTests.Features.BackupRestore;

internal static class ClusterRestoreRf3MissingProgress
{
    private const long MinimumProgressBytes = 1;

    internal static async Task RequireAsync(ClusterRestoreRf3Fixture target, bool published,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(ClusterRestoreRf3RetainedCut.OperationRoot(target),
            ClusterRestoreRf3ResumeProtocol.ProgressFile);
        var file = new FileInfo(path);
        if (!file.Exists || file.Length < MinimumProgressBytes
            || file.Length > IntegrationExecutionOptions.StorageExecution().Value.MaximumBackupManifestBytes
            || (file.Attributes & FileAttributes.ReparsePoint) != default)
        { throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid); }
        var original = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        var originalOperation = ClusterRestoreRf3RetainedCut.Operation(target);
        var targetCut = published ? ClusterRestoreRf3RetainedCut.Target(target) : null;
        Exception? primary = null;
        var cleanupCompleted = false;
        var removed = false;
        try
        {
            try
            {
                File.Delete(path);
                removed = true;
                var missingOperation = ClusterRestoreRf3RetainedCut.Operation(target);
                await target.StartRejectedMissingProgressAsync(published, cancellationToken).ConfigureAwait(false);
                await Assert.That(File.Exists(path)).IsFalse();
                await Assert.That(ClusterRestoreRf3RetainedCut.Operation(target).SequenceEqual(missingOperation)).IsTrue();
                await RequireTargetAsync(target, targetCut).ConfigureAwait(false);
            }
            catch (Exception failure) { primary = failure; throw; }
            finally
            {
                if (removed)
                { Restore(path, original); }
                cleanupCompleted = true;
            }
        }
        catch (Exception terminal)
        {
            if (primary is not null && !cleanupCompleted)
            { throw new AggregateException(primary, terminal); }
            throw;
        }
        await Assert.That(ClusterRestoreRf3RetainedCut.Operation(target).SequenceEqual(originalOperation)).IsTrue();
        await RequireTargetAsync(target, targetCut).ConfigureAwait(false);
    }

    private static async Task RequireTargetAsync(ClusterRestoreRf3Fixture target, byte[]? targetCut)
    {
        if (targetCut is not null)
        { await Assert.That(ClusterRestoreRf3RetainedCut.Target(target).SequenceEqual(targetCut)).IsTrue(); }
        else
        {
            await Assert.That(Directory.Exists(target.DataRoot)).IsFalse();
            await Assert.That(File.Exists(target.DataRoot)).IsFalse();
        }
    }

    private static void Restore(string path, byte[] original)
    {
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        file.Write(original);
        file.Flush(flushToDisk: true);
    }
}
