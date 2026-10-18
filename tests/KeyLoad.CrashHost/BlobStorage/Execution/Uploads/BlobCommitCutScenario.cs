using ManagedCode.Communication.Commands;
using KeyLoad.Core;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost.Features.BlobStorage;

internal static class BlobCommitCutScenario
{
    private const string SelectedIoFault = "Selected genuine blob JournalFlushed I/O fault.";
    private const string StagedUploadAbsent = "The acknowledged staged blob upload is absent.";
    private const string UnknownOutcomeAbsent = "The native publication did not return its genuine unknown outcome.";
    private const string PoisonAbsent = "The genuine uncertain native owner was not poisoned.";
    private const int RequiredArgumentCount = 4;
    private const int ModeArgumentIndex = 3;
    private const int RootArgumentIndex = 0;
    private const int CutArgumentIndex = 1;
    private const long UnselectedPosition = 0;
    private const long NextPositionOffset = 1;
    private const long PublishedRevision = 1;
    private const int PoisonProbeRecordLimit = 1;

    internal static async Task<bool> TryRunAsync(string[] args)
    {
        if (args.Length != RequiredArgumentCount || args[ModeArgumentIndex] is not (BlobCommitCutProtocol.CompleteMode or BlobCommitCutProtocol.DeleteMode))
        {
            return false;
        }
        var root = args[RootArgumentIndex];
        var cut = Enum.Parse<BlobCommitCutKind>(args[CutArgumentIndex]);
        var complete = args[ModeArgumentIndex] == BlobCommitCutProtocol.CompleteMode;
        var selectedPosition = UnselectedPosition;
        var injected = false;
        CommitStage? injectedStage = null;
        var injectedPosition = UnselectedPosition;
        void Observe(CommitStage stage, long position, int index)
        {
            if (position != selectedPosition || selectedPosition == UnselectedPosition)
            {
                return;
            }
            if (cut == BlobCommitCutKind.UnknownAfterJournalFlush && stage == CommitStage.JournalFlushed)
            {
                injected = true;
                injectedStage = stage;
                injectedPosition = position;
                throw new IOException(SelectedIoFault);
            }
            if ((cut == BlobCommitCutKind.HeaderWritten && stage == CommitStage.HeaderWritten)
                || (cut == BlobCommitCutKind.JournalFlushed && stage == CommitStage.JournalFlushed))
            {
                CrashHostPause.AtBoundary();
            }
        }
        using var store = new ZoneTreeStore(new(root)
        {
            FaultObserver = Observe
        },
            CrashExecutionOptions.StorageExecution(), CrashExecutionOptions.PointCacheExecution());
        var database = await BlobCommitCutSeed.WriteAsync(root, store, complete).ConfigureAwait(false);
        var operation = Selected(database, complete);
        await File.WriteAllBytesAsync(Path.Combine(root, BlobCommitCutProtocol.OperationFile),
            NativeSerialization.Serialize(operation)).ConfigureAwait(false);
        selectedPosition = checked(store.Position + NextPositionOffset);
        if (cut == BlobCommitCutKind.UnknownAfterJournalFlush)
        {
            await ObserveUnknownAsync(root, store, database, operation, () => (injected, injectedStage, injectedPosition)).ConfigureAwait(false);
            await WaitForUnknownAsync().ConfigureAwait(false);
        }
        else
        {
            var result = database.Apply(operation);
            _ = result.Get<BlobCommitResult<BlobMetadata>>();
            await File.WriteAllBytesAsync(Path.Combine(root, BlobCommitCutProtocol.ReturnedFile),
                NativeSerialization.Serialize(result)).ConfigureAwait(false);
            await CrashHostPause.WaitForKillAsync();
        }
        return true;
    }

    private static async Task WaitForUnknownAsync()
    {
        using var output = Console.OpenStandardOutput();
        var signal = System.Text.Encoding.UTF8.GetBytes(BlobCommitCutProtocol.UnknownMarker + Environment.NewLine);
        await output.WriteAsync(signal).ConfigureAwait(false);
        await output.FlushAsync().ConfigureAwait(false);
        await Task.Delay(Timeout.InfiniteTimeSpan, TimeProvider.System).ConfigureAwait(false);
    }

    private static Command<DatabaseCommand> Selected(DatabaseEngine database, bool complete)
    {
        var id = complete ? BlobCommitCutProtocol.CompleteId : BlobCommitCutProtocol.DeleteId;
        var payload = complete
            ? NativeSerialization.Serialize(new CompleteBlobUploadRequest(id, BlobCommitCutProtocol.Blob,
                BlobCommitCutProtocol.NewUpload, new BlobStorageOperations(database).UploadInfo(BlobCommitCutProtocol.Principal,
                    new(BlobCommitCutProtocol.Blob, BlobCommitCutProtocol.NewUpload))?.IntegrityHash
                    ?? throw new InvalidOperationException(StagedUploadAbsent)))
            : NativeSerialization.Serialize(new DeleteBlobRequest(id, BlobCommitCutProtocol.Blob, PublishedRevision));
        return database.CreateCommand(complete ? OperationKind.CompleteBlobUpload : OperationKind.DeleteBlob,
            id, BlobCommitCutProtocol.Principal, database.EvaluationClock.GetUtcNow(), payload);
    }

    private static async Task ObserveUnknownAsync(string root, ZoneTreeStore store, DatabaseEngine database,
        Command<DatabaseCommand> operation, Func<(bool Injected, CommitStage? Stage, long Position)> injected)
    {
        KeyLoadException? observed = null;
        try
        {
            _ = database.Apply(operation);
        }
        catch (KeyLoadException failure) when (failure.Code == ErrorCode.UnknownWriteOutcome) { observed = failure; }
        var fault = injected();
        if (!fault.Injected || fault.Stage is null || observed is null)
        {
            throw new InvalidOperationException(UnknownOutcomeAbsent);
        }
        KeyLoadException? poison = null;
        try
        {
            _ = store.Read(view => view.Scan([], PoisonProbeRecordLimit));
        }
        catch (KeyLoadException failure) when (failure.Code == ErrorCode.RecoveryRequired) { poison = failure; }
        if (poison is null)
        {
            throw new InvalidOperationException(PoisonAbsent);
        }
        await File.WriteAllBytesAsync(Path.Combine(root, BlobCommitCutProtocol.FaultFile),
            JsonDefaults.Serialize(new BlobCommitCutObservedFault(observed.Code, observed.Message,
                poison.Code, poison.Message, fault.Stage.Value, fault.Position, fault.Injected))).ConfigureAwait(false);
    }
}
