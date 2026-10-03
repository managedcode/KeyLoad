using System.Runtime.ExceptionServices;
using System.Security.Cryptography;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeFormatUpgradeBuilder
{
    internal static StoreIdentity Rebuild(ZoneTreeFormatUpgradeSource source, string stagePath,
        ZoneTreeStoreOptions destinationOptions)
    {
        ZoneTreeFormatUpgradeStage.VerifySourceCopies(stagePath, source);
        var stageOptions = StageOptions(destinationOptions, stagePath, source.Identity);
        var identity = source.Identity with { FormatVersion = ZoneTreePersistenceFormat.CurrentDataEpoch };
        ZoneTreeIdentityFile.Write(Path.Combine(stagePath, ZoneTreePersistenceFormat.IdentityFileName), identity);
        var runtime = new ZoneTreeStoreRuntime(stageOptions);
        var checkpointPath = Path.Combine(stagePath, ZoneTreeFormatUpgradeStage.CheckpointTemporaryFileName);
        BuildTree(runtime, source, stagePath, stageOptions, checkpointPath);
        File.Move(checkpointPath, Path.Combine(stagePath, ZoneTreePersistenceFormat.JournalFileName), true);
        return VerifyBuiltTarget(stagePath, source.Identity, stageOptions, source.Position);
    }

    private static void BuildTree(ZoneTreeStoreRuntime runtime, ZoneTreeFormatUpgradeSource source,
        string stagePath, ZoneTreeStoreOptions options, string checkpointPath)
    {
        Exception? failure = null;
        try
        {
            BuildTreeCore(runtime, source, stagePath, options, checkpointPath);
        }
        catch (Exception error)
        {
            failure = error;
            throw;
        }
        finally
        {
            DisposePreservingFailure(runtime, failure);
        }
    }

    private static void BuildTreeCore(ZoneTreeStoreRuntime runtime, ZoneTreeFormatUpgradeSource source,
        string stagePath, ZoneTreeStoreOptions options, string checkpointPath)
    {
        var replayPosition = Replay(runtime, source, stagePath, options);
        if (replayPosition != source.Position)
        {
            throw Errors.Fail(ErrorCode.Corruption, ZoneTreePersistenceFormat.JournalSequenceInvalid);
        }
        var applied = ReadAppliedPosition(runtime);
        var snapshot = ZoneTreeCheckpointWriter.Write(checkpointPath, options, runtime.Identity,
            source.Position, applied, runtime.Tree);
        options.FaultObserver?.Invoke(CommitStage.UpgradeCheckpointFlushed, source.Position, 0);
        if (snapshot.Position != source.Position || snapshot.AppliedPosition != applied)
        {
            throw Errors.Fail(ErrorCode.Corruption, ZoneTreePersistenceFormat.CheckpointVerificationFailed);
        }
    }

    private static long Replay(ZoneTreeStoreRuntime runtime, ZoneTreeFormatUpgradeSource source,
        string stagePath, ZoneTreeStoreOptions options)
    {
        var sourceJournal = ZoneTreeFormatUpgradeStage.SourceCopyPath(stagePath, ZoneTreePersistenceFormat.JournalFileName);
        using var journal = new FileStream(sourceJournal, FileMode.Open, FileAccess.Read, FileShare.None,
            ZoneTreePersistenceFormat.FileBufferBytes, FileOptions.SequentialScan);
        if (!string.Equals(ZoneTreeFormatUpgradeStage.Digest(journal), source.JournalDigest, StringComparison.Ordinal))
        {
            throw Errors.Fail(ErrorCode.Corruption, ZoneTreePersistenceFormat.BackupFileVerificationFailed);
        }
        journal.Position = 0;
        var position = ZoneTreeFormatUpgradeJournal.ReplaySource(journal, options, runtime);
        options.FaultObserver?.Invoke(CommitStage.UpgradeRecovered, position, 0);
        return position;
    }

    private static long ReadAppliedPosition(ZoneTreeStoreRuntime runtime)
    {
        var key = KeyCodec.Encode(ZoneTreePersistenceFormat.SystemNamespace, ZoneTreePersistenceFormat.LastAppliedKey);
        return runtime.View.ReadOwnedValue(key) is { } bytes ? NativeSerialization.Deserialize<long>(bytes) : 0;
    }

    private static void DisposePreservingFailure(ZoneTreeStoreRuntime runtime, Exception? failure)
    {
        try
        {
            runtime.Dispose();
        }
        catch (Exception cleanup) when (failure is not null)
        {
            throw new AggregateException(failure, cleanup);
        }
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    internal static StoreIdentity VerifyTarget(string directory, StoreIdentity sourceIdentity,
        ZoneTreeStoreOptions options, long expectedPosition)
    {
        return VerifyTargetCore(directory, sourceIdentity, options, expectedPosition,
            minimumPosition: null, allowMaintenanceChanges: false);
    }

    internal static StoreIdentity VerifyPublishedTarget(string directory, StoreIdentity sourceIdentity,
        long sourcePosition, ZoneTreeStoreOptions options)
    {
        return VerifyTargetCore(directory, sourceIdentity, options, expectedPosition: null,
            minimumPosition: sourcePosition, allowMaintenanceChanges: true);
    }

    private static StoreIdentity VerifyTargetCore(string directory, StoreIdentity sourceIdentity,
        ZoneTreeStoreOptions options, long? expectedPosition, long? minimumPosition, bool allowMaintenanceChanges)
    {
        ZoneTreeFormatUpgradePathSafety.VerifyNoLinks(directory, allowMissingFinal: false);
        var ownerPath = Path.Combine(directory, ZoneTreePersistenceFormat.OwnerLockFileName);
        ZoneTreeFormatUpgradeReceiptFile.VerifyRegularFile(ownerPath);
        using var ownership = new FileStream(ownerPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var identityPath = Path.Combine(directory, ZoneTreePersistenceFormat.IdentityFileName);
        var identity = ZoneTreeIdentityFile.Read(identityPath);
        VerifyIdentity(sourceIdentity, identity, allowMaintenanceChanges);
        using (var journal = new FileStream(Path.Combine(directory, ZoneTreePersistenceFormat.JournalFileName),
            FileMode.Open, FileAccess.Read, FileShare.Read, ZoneTreePersistenceFormat.FileBufferBytes,
            FileOptions.SequentialScan))
        {
            var position = ZoneTreeFormatUpgradeJournal.ValidateCurrent(journal, options, identity.Incarnation);
            if (expectedPosition is { } expected && position != expected)
            {
                throw Errors.Fail(ErrorCode.Corruption, ZoneTreePersistenceFormat.JournalSequenceInvalid);
            }
            if (minimumPosition is { } minimum && position < minimum)
            {
                throw Errors.Fail(ErrorCode.Corruption, ZoneTreePersistenceFormat.JournalSequenceInvalid);
            }
        }
        return identity;
    }

    private static StoreIdentity VerifyBuiltTarget(string directory, StoreIdentity sourceIdentity,
        ZoneTreeStoreOptions options, long expectedPosition)
    {
        _ = VerifyTarget(directory, sourceIdentity, options, expectedPosition);
        using var store = new ZoneTreeStore(options);
        VerifyIdentity(sourceIdentity, store.Identity, allowMaintenanceChanges: false);
        if (store.Position != expectedPosition)
        {
            throw Errors.Fail(ErrorCode.Corruption, ZoneTreePersistenceFormat.JournalSequenceInvalid);
        }
        return store.Identity;
    }

    private static void VerifyIdentity(StoreIdentity source, StoreIdentity target, bool allowMaintenanceChanges)
    {
        if (target.FormatVersion != ZoneTreePersistenceFormat.CurrentDataEpoch
            || target.KeyCodecVersion != source.KeyCodecVersion || target.NodeId != source.NodeId
            || target.Incarnation != source.Incarnation || target.Durability != source.Durability
            || !allowMaintenanceChanges && (target.DispatchPaused != source.DispatchPaused
                || target.ReadGeneration != source.ReadGeneration)
            || allowMaintenanceChanges && target.ReadGeneration < source.ReadGeneration
            || !CryptographicOperations.FixedTimeEquals(target.SigningKey.Span, source.SigningKey.Span))
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, ZoneTreePersistenceFormat.IdentityFormatUnsupported);
        }
    }

    private static ZoneTreeStoreOptions StageOptions(ZoneTreeStoreOptions options, string directory,
        StoreIdentity identity) => options with
    {
        Directory = directory,
        Incarnation = identity.Incarnation,
        SigningKey = identity.SigningKey,
        EmbeddedPointCache = null
    };
}
