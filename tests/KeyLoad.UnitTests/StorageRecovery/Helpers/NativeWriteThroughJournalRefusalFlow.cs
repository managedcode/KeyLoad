using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class NativeWriteThroughJournalRefusalFlow
{
    private const string MissingType = "System.IO.FileNotFoundException";

    internal static async Task RunAsync(NativeJournalEntryKind kind)
    {
        var failures = new List<Exception>();
        var lifetime = new StorageFailureOwnerLifetime();
        ZoneTreeExistingStoreFixture? files = null;
        ServerFailureObserver.Observe(() => files = new(), failures);
        if (files is not null)
        {
            var progress = new NativeJournalRefusalProgress();
            await ServerFailureObserver.ObserveAsync(() => RefuseAndRepairAsync(files, kind, progress), failures);
            if (progress.CanAdvance && lifetime.CanAdvance)
            {
                await ServerFailureObserver.ObserveAsync(
                    () => NativeWriteThroughJournalFlow.ContinueAsync(files, lifetime), failures);
            }
            if (progress.CanAdvance && lifetime.CanAdvance)
            {
                ServerFailureObserver.Observe(files.Dispose, failures);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RefuseAndRepairAsync(ZoneTreeExistingStoreFixture files,
        NativeJournalEntryKind kind, NativeJournalRefusalProgress progress)
    {
        var failures = new List<Exception>();
        var original = await files.CaptureStoreEntriesAsync();
        var entry = new NativeJournalRefusalFiles(files);
        ServerFailureObserver.Observe(() => entry.Change(kind), failures);
        if (entry.IsHidden && failures.Count == 0)
        {
            progress.CanAdvance = false;
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var result = await files.InspectAsync(cancellationToken: TestContext.Current!.Execution.CancellationToken);
                progress.CanAdvance = result.ProcessReaped && result.StdoutReaderSettled && result.StderrReaderSettled
                    && result.OuterOwnerReleased;
                await ExistingStoreInspectionAssertions.FailedAsync(result,
                    kind == NativeJournalEntryKind.Missing ? null : nameof(ErrorCode.FormatUnsupported),
                    kind == NativeJournalEntryKind.Missing ? MissingType : null);
                await entry.RequireOriginalsAsync(original, kind);
            }, failures);
        }
        if (progress.CanAdvance && entry.IsHidden)
        {
            ServerFailureObserver.Observe(entry.Restore, failures);
            progress.CanAdvance = !entry.IsHidden;
        }
        if (progress.CanAdvance && failures.Count == 0)
        {
            await ServerFailureObserver.ObserveAsync(() => files.AssertStoreEntriesUnchangedAsync(original), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
