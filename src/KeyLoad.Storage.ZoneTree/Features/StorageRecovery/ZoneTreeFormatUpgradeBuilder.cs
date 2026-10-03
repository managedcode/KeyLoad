using System.Runtime.ExceptionServices;
using System.Security.Cryptography;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeFormatUpgradeBuilder
{
    internal static StoreIdentity Rebuild(ZoneTreeFormatUpgradeSource source, string stagePath,
        ZoneTreeStoreOptions destinationOptions)
    {
        var stageOptions = StageOptions(destinationOptions, stagePath, source.Identity);
        var identity = source.Identity with { FormatVersion = ZoneTreePersistenceFormat.CurrentDataEpoch };
        ZoneTreeIdentityFile.Write(Path.Combine(stagePath, ZoneTreePersistenceFormat.IdentityFileName), identity);
        var runtime = new ZoneTreeStoreRuntime(stageOptions);
        var checkpointPath = Path.Combine(stagePath, ZoneTreeFormatUpgradeStage.CheckpointTemporaryFileName);
        var failure = BuildTree(runtime, source, stagePath, stageOptions, checkpointPath);
        DisposePreservingFailure(runtime, failure);
        File.Move(checkpointPath, Path.Combine(stagePath, ZoneTreePersistenceFormat.JournalFileName), true);
        return VerifyBuiltTarget(stagePath, source.Identity, stageOptions, source.Position);
    }

    private static Exception? BuildTree(ZoneTreeStoreRuntime runtime, ZoneTreeFormatUpgradeSource source,
        string stagePath, ZoneTreeStoreOptions options, string checkpointPath)
    {
        try
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
            return null;
        }
        catch (Exception error)
        {
            return error;
        }
    }

    private static long Replay(ZoneTreeStoreRuntime runtime, ZoneTreeFormatUpgradeSource source,
        string stagePath, ZoneTreeStoreOptions options)
    {
        var sourceJournal = ZoneTreeFormatUpgradeStage.SourceCopyPath(stagePath, ZoneTreePersistenceFormat.JournalFileName);
        using var journal = new FileStream(sourceJournal, FileMode.Open, FileAccess.Read, FileShare.Read,
            ZoneTreePersistenceFormat.FileBufferBytes, FileOptions.SequentialScan);
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
        ZoneTreeStoreOptions options, long? expectedPosition = null)
    {
        var ownerPath = Path.Combine(directory, ZoneTreePersistenceFormat.OwnerLockFileName);
        ZoneTreeFormatUpgradeReceiptFile.VerifyRegularFile(ownerPath);
        using var ownership = new FileStream(ownerPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var identityPath = Path.Combine(directory, ZoneTreePersistenceFormat.IdentityFileName);
        var identity = ZoneTreeIdentityFile.Read(identityPath);
        VerifyIdentity(sourceIdentity, identity);
        using (var journal = new FileStream(Path.Combine(directory, ZoneTreePersistenceFormat.JournalFileName),
            FileMode.Open, FileAccess.Read, FileShare.Read, ZoneTreePersistenceFormat.FileBufferBytes,
            FileOptions.SequentialScan))
        {
            var position = ZoneTreeFormatUpgradeJournal.ValidateCurrent(journal, options);
            if (expectedPosition is { } expected && position != expected)
            {
                throw Errors.Fail(ErrorCode.Corruption, ZoneTreePersistenceFormat.JournalSequenceInvalid);
            }
        }
        return identity;
    }

    private static StoreIdentity VerifyBuiltTarget(string directory, StoreIdentity sourceIdentity,
        ZoneTreeStoreOptions options, long expectedPosition)
    {
        var identity = VerifyTarget(directory, sourceIdentity, options, expectedPosition);
        using var store = new ZoneTreeStore(options);
        VerifyIdentity(sourceIdentity, store.Identity);
        if (store.Position != expectedPosition)
        {
            throw Errors.Fail(ErrorCode.Corruption, ZoneTreePersistenceFormat.JournalSequenceInvalid);
        }
        return store.Identity;
    }

    private static void VerifyIdentity(StoreIdentity source, StoreIdentity target)
    {
        if (target.FormatVersion != ZoneTreePersistenceFormat.CurrentDataEpoch
            || target.KeyCodecVersion != source.KeyCodecVersion || target.NodeId != source.NodeId
            || target.Incarnation != source.Incarnation || target.Durability != source.Durability
            || target.DispatchPaused != source.DispatchPaused || target.ReadGeneration != source.ReadGeneration
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
