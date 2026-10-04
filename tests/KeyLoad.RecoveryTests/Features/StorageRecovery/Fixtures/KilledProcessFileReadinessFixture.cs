using System.Runtime.ExceptionServices;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.StorageRecovery;

internal sealed class KilledProcessFileReadinessFixture : IAsyncDisposable
{
    private const string ReadinessKey = "killed-process-readiness";
    private const string ReopenedKey = "killed-process-readiness-reopened";
    private const string TreeDirectoryName = "tree";
    private const string ParkedTreeDirectoryName = "tree-readiness-parked";
    private const string MetadataWalName = "0.meta.wal";
    private const string ParkedMetadataWalName = "0.meta.wal-readiness-parked";
    private readonly CancellationTokenSource cancellation = new();
    private FileStream? heldFile;
    private Task? readiness;
    private bool readinessObserved;
    private bool preservingOriginalFailure;
    private int disposeStarted;

    internal KilledProcessFileReadinessFixture()
    {
        Root = Path.Combine(Path.GetTempPath(), "keyload-readiness-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var store = new ZoneTreeStore(new(Root));
            store.Commit((transaction, position) =>
            {
                transaction.PutRecord(KeyCodec.Encode(ReadinessKey), true);
                return position;
            });
        }
        catch (Exception)
        {
            try
            { DeleteStoreRoot(); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            cancellation.Dispose();
            throw;
        }
    }

    internal string Root { get; }

    internal void HoldFile(string relativePath) => heldFile = new FileStream(
        Path.Combine(Root, relativePath), FileMode.Open, FileAccess.ReadWrite, FileShare.None);

    internal Task StartReadiness() => readiness =
        StorageRecoveryProcessTests.WaitForKilledProcessFilesAsync(Root, cancellation.Token);

    internal Task CancelAsync() => cancellation.CancelAsync();

    internal void MoveTreeAside() => Directory.Move(
        Path.Combine(Root, TreeDirectoryName), Path.Combine(Root, ParkedTreeDirectoryName));

    internal void RestoreTree() => Directory.Move(
        Path.Combine(Root, ParkedTreeDirectoryName), Path.Combine(Root, TreeDirectoryName));

    internal void MoveMetadataWalAside() => File.Move(
        Path.Combine(Root, TreeDirectoryName, MetadataWalName),
        Path.Combine(Root, TreeDirectoryName, ParkedMetadataWalName));

    internal void RestoreMetadataWal() => File.Move(
        Path.Combine(Root, TreeDirectoryName, ParkedMetadataWalName),
        Path.Combine(Root, TreeDirectoryName, MetadataWalName));

    internal bool ReopenAndCommit()
    {
        using var store = new ZoneTreeStore(new(Root));
        var originalRecordExists = store.Read(view => view.ReadOwnedValue(KeyCodec.Encode(ReadinessKey)) is not null);
        store.Commit((transaction, _) =>
        {
            transaction.PutRecord(KeyCodec.Encode(ReopenedKey), true);
            return true;
        });
        var reopenedRecordExists = store.Read(view => view.ReadOwnedValue(KeyCodec.Encode(ReopenedKey)) is not null);
        return originalRecordExists && reopenedRecordExists;
    }

    internal void ReleaseFile()
    {
        heldFile?.Dispose();
        heldFile = null;
    }

    internal void MarkReadinessObserved() => readinessObserved = true;

    internal async Task RunAsync(Func<KilledProcessFileReadinessFixture, Task> scenario)
    {
        try
        {
            await scenario(this);
        }
        catch (Exception)
        {
            preservingOriginalFailure = true;
            try
            { await DisposeAsync(); }
            catch (IOException) { }
            catch (OperationCanceledException) { }
            catch (AggregateException) { }
            catch (ObjectDisposedException) { }
            catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposeStarted, 1) != 0)
        {
            return;
        }

        Exception? cleanupFailure = null;
        await CancelReadinessAsync(error => cleanupFailure ??= error);
        ReleaseHeldFile(ref cleanupFailure);
        await ObservePendingReadinessAsync(error => cleanupFailure ??= error);
        DeleteRoot(ref cleanupFailure);
        cancellation.Dispose();
        if (cleanupFailure is not null)
        {
            ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
        }
    }

    private async Task CancelReadinessAsync(Action<Exception> captureFailure)
    {
        try
        { await cancellation.CancelAsync(); }
        catch (ObjectDisposedException error) { captureFailure(error); }
    }

    private void ReleaseHeldFile(ref Exception? cleanupFailure)
    {
        try
        { ReleaseFile(); }
        catch (IOException error) { cleanupFailure ??= error; }
        catch (ObjectDisposedException error) { cleanupFailure ??= error; }
    }

    private async Task ObservePendingReadinessAsync(Action<Exception> captureFailure)
    {
        if (readiness is null || readinessObserved)
        {
            return;
        }

        try
        { await readiness; }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (IOException error) when (preservingOriginalFailure) { captureFailure(error); }
        catch (AggregateException error) when (preservingOriginalFailure) { captureFailure(error); }
        catch (ObjectDisposedException error) when (preservingOriginalFailure) { captureFailure(error); }
    }

    private void DeleteRoot(ref Exception? cleanupFailure)
    {
        try
        { DeleteStoreRoot(); }
        catch (IOException error) { cleanupFailure ??= error; }
        catch (UnauthorizedAccessException error) { cleanupFailure ??= error; }
    }

    private void DeleteStoreRoot()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, true);
        }
    }
}
