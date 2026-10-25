using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.StorageRecovery;

internal static class WriteThroughJournalRefusalFlow
{
    private const string MissingType = "System.IO.FileNotFoundException";

    internal static async Task RunAsync(JournalEntryKind kind)
    {
        var failures = new List<Exception>();
        var lifetime = new StorageFailureOwnerLifetime();
        ZoneTreeExistingStoreFixture? files = null;
        ServerFailureObserver.Observe(() => files = new(), failures);
        if (files is not null)
        {
            var progress = new JournalRefusalProgress();
            await ServerFailureObserver.ObserveAsync(() => RefuseAndRepairAsync(files, kind, progress), failures);
            if (progress.CanAdvance && lifetime.CanAdvance)
            {
                await ServerFailureObserver.ObserveAsync(
                    () => WriteThroughJournalFlow.ContinueAsync(files, lifetime), failures);
            }
            if (progress.CanAdvance && lifetime.CanAdvance)
            {
                ServerFailureObserver.Observe(files.Dispose, failures);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RefuseAndRepairAsync(ZoneTreeExistingStoreFixture files,
        JournalEntryKind kind, JournalRefusalProgress progress)
    {
        var failures = new List<Exception>();
        var original = await files.CaptureStoreEntriesAsync();
        var entry = new JournalRefusalFiles(files);
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
                    kind == JournalEntryKind.Missing ? null : nameof(ErrorCode.FormatUnsupported),
                    kind == JournalEntryKind.Missing ? MissingType : null);
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
