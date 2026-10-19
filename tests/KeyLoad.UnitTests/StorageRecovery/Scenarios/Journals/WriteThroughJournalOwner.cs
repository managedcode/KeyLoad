using System.ComponentModel;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.Storage.ZoneTree.IO;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class WriteThroughJournalOwner
{
    private const int LinuxWouldBlock = 11;
    private const int MacWouldBlock = 35;
    private const string MissingCause = "The original execution lock refusal did not retain its execution cause.";

    internal static async Task RunAsync(ZoneTreeExistingStoreFixture files,
        StorageFailureOwnerLifetime lifetime, int originalBclFlags, bool publish)
    {
        var failures = new List<Exception>();
        ZoneTreeStoreRuntime? pending = null;
        ZoneTreeStoreRuntime? original = null;
        ZoneTreeStore? store = null;
        ServerFailureObserver.Observe(() =>
        {
            pending = lifetime.Acquire(() => new ZoneTreeStoreRuntime(
                files.Options.ResolveExecutionOptions(UnitExecutionOptions.StorageExecution())), failures);
            if (pending is null)
            {
                return;
            }
            store = new(pending, pending.Identity.NodeId);
            lifetime.Transfer(pending, store);
            original = pending;
            pending = null;
        }, failures);
        if (store is not null)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await WriteThroughJournalCut.FlagsAsync(original!.Journal, originalBclFlags);
                await Assert.That(original.Journal.Name).IsEqualTo(Path.GetFullPath(files.JournalPath));
                await RequireHeldAsync(files, lifetime);
                if (publish)
                {
                    await WriteThroughJournalCut.RowsAsync(store, files, healthy: false);
                    WriteThroughJournalCut.CommitHealthy(store);
                }
                await WriteThroughJournalCut.RowsAsync(store, files, healthy: true);
            }, failures);
            lifetime.Close(store, failures);
        }
        if (pending is not null)
        {
            lifetime.Close(pending, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RequireHeldAsync(ZoneTreeExistingStoreFixture files,
        StorageFailureOwnerLifetime lifetime)
    {
        var buffer = UnitExecutionOptions.StorageExecution().Value.FileBufferBytes;
        var failures = new List<Exception>();
        FileStream? unexpected = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var refused = Assert.ThrowsExactly<IOException>(() =>
                unexpected = OfflineRegularFile.Open(files.JournalPath, FileAccess.Read, FileShare.None, buffer));
            var cause = refused.InnerException as Win32Exception ?? throw new InvalidDataException(MissingCause);
            await Assert.That(cause.NativeErrorCode).IsEqualTo(OperatingSystem.IsMacOS() ? MacWouldBlock : LinuxWouldBlock);
        }, failures);
        if (unexpected is not null)
        {
            var retained = lifetime.Acquire(() => unexpected, failures);
            if (retained is not null)
            {
                lifetime.Close(retained, failures);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
