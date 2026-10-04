using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

internal sealed class ZoneTreeCheckpointManager(ZoneTreeStoreRuntime runtime)
{
    internal StorageSnapshot CreateSnapshot(string path, long? expectedAppliedPosition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        runtime.Gate.EnterReadLock();
        try
        {
            runtime.Check();
            return WriteCurrent(path, expectedAppliedPosition);
        }
        finally
        {
            runtime.Gate.ExitReadLock();
        }
    }

    private StorageSnapshot WriteCurrent(string path, long? expectedAppliedPosition)
    {
        var applied = AppliedPosition();
        if (expectedAppliedPosition is { } expected && applied != expected)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, SnapshotCutLost);
        }

        return ZoneTreeCheckpointWriter.Write(path, runtime.Options, runtime.Identity,
            runtime.Position, applied, runtime.Tree);
    }

    private long AppliedPosition() => runtime.View.ReadOwnedValue(KeyCodec.Encode(SystemNamespace, LastAppliedKey)) is { } value
        ? NativeSerialization.Deserialize<long>(value) : 0;

    internal StorageSnapshot Compact()
    {
        runtime.Gate.EnterWriteLock();
        var temporary = TemporaryPath(CheckpointTemporaryPrefix);
        try
        {
            runtime.Check();
            var snapshot = WriteCurrent(temporary, null);
            using (var verify = File.OpenRead(temporary))
            {
                ZoneTreeCheckpointReader.Read(verify, runtime.Options);
            }

            ZoneTreeCheckpointGeneration.Replace(runtime, temporary, snapshot.Position, replaceTree: false);
            return snapshot;
        }
        finally
        {
            DeleteTemporaryAndExitGate(temporary);
        }
    }

    internal StorageSnapshot InstallSnapshot(string path, long expectedAppliedPosition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        // Verify the transfer before changing live state or closing the old generation.
        var metadata = VerifySnapshot(path);
        if (metadata.Incarnation != runtime.Identity.Incarnation || metadata.AppliedPosition != expectedAppliedPosition)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, SnapshotScopeInvalid);
        }

        runtime.Gate.EnterWriteLock();
        var temporary = TemporaryPath(InstallTemporaryPrefix);
        try
        {
            runtime.Check();
            if (expectedAppliedPosition < AppliedPosition())
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, StaleSnapshot);
            }

            runtime.NativeReadCuts.RequireNoActiveLeaseForTreeReplacement();

            File.Copy(path, temporary, false);
            var snapshot = ReadStaged(temporary, expectedAppliedPosition);
            ZoneTreeCheckpointGeneration.Replace(runtime, temporary, snapshot.Position, replaceTree: true);
            return snapshot;
        }
        finally
        {
            DeleteTemporaryAndExitGate(temporary);
        }
    }

    private StorageSnapshot ReadStaged(string temporary, long expectedAppliedPosition)
    {
        using var input = new FileStream(temporary, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var snapshot = ZoneTreeCheckpointReader.Read(input, runtime.Options);
        if (snapshot.Incarnation != runtime.Identity.Incarnation || snapshot.AppliedPosition != expectedAppliedPosition
            || input.Position != input.Length)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, StagedSnapshotScopeInvalid);
        }

        runtime.Options.FaultObserver?.Invoke(CommitStage.SnapshotWritten, snapshot.Position, 0);
        input.Flush(true);
        runtime.Options.FaultObserver?.Invoke(CommitStage.SnapshotFlushed, snapshot.Position, 0);
        return snapshot;
    }

    internal StorageSnapshot VerifySnapshot(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var input = File.OpenRead(path);
        var snapshot = ZoneTreeCheckpointReader.Read(input, runtime.Options);
        if (input.Position != input.Length)
        {
            throw Errors.Fail(ErrorCode.Corruption, SnapshotTrailingData);
        }

        return snapshot;
    }

    private string TemporaryPath(string prefix) => Path.Combine(runtime.Options.Directory,
        prefix + Guid.NewGuid().ToString(GuidFormat) + TemporaryFileSuffix);

    private void DeleteTemporaryAndExitGate(string temporary)
    {
        try
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
        finally
        {
            runtime.Gate.ExitWriteLock();
        }
    }
}
