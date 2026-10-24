using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using P = KeyLoad.UnitTests.BackupRestore.ArchiveEntryProtocol;

namespace KeyLoad.UnitTests.BackupRestore;

internal sealed class ArchiveEntryFixture
{
    internal string Root { get; } = Path.Combine(AppContext.BaseDirectory, P.Prefix + Guid.NewGuid().ToString(P.GuidFormat));
    internal TestDatabase Source
    {
        get;
    }
    internal string Backup => Path.Combine(Root, P.Backup);
    internal string Target => Path.Combine(Root, P.Target);
    internal string SourcePath => Path.Combine(Root, P.Source);
    internal bool ChildClosed { get; private set; } = true;
    private bool sourceAttempted;
    private bool sourceClosed;
    private bool uncertain;
    private ZoneTreeStore? opened;
    private bool closeAttempted;
    internal ZoneTreeStore OpenedStore() => opened ?? throw new InvalidOperationException("The execution target owner is absent.");
    internal bool CanOpen => sourceClosed && !uncertain && opened is null;

    internal ArchiveEntryFixture() => Source = new(directory: SourcePath);
    internal void RetainRoot() => uncertain = true;

    internal void CloseSource(List<Exception> failures)
    {
        if (sourceAttempted)
        {
            return;
        }
        sourceAttempted = true;
        ServerFailureObserver.Observe(() =>
        {
            Source.JoinOwnedResources();
            sourceClosed = true;
        }, failures);
        uncertain |= !sourceClosed;
    }

    internal async Task<CliBackupRestoreProcessResult> ChildAsync(string[] arguments, CancellationToken token)
    {
        ChildClosed = false;
        try
        {
            var result = await CliBackupRestoreProcess.RunAsync(CliBackupRestoreProcess.CaptureExecutionOptions(), arguments, token);
            ChildClosed = result.OriginalExitJoined && result.StandardOutputJoined && result.StandardErrorJoined && result.ProcessDisposed;
            uncertain |= !ChildClosed;
            return result;
        }
        catch (Exception)
        {
            uncertain = true;
            throw;
        }
    }

    internal ZoneTreeStore Open(string directory, StoreIdentity identity)
    {
        if (!CanOpen)
        {
            throw new InvalidOperationException("The original archive fixture owner is not closed.");
        }
        try
        {
            opened = ZoneTreeExistingStore.Open(new(directory)
            {
                Incarnation = identity.Incarnation
            }, identity.NodeId, Source.StorageExecution);
            closeAttempted = false;
            return opened;
        }
        catch (Exception)
        {
            uncertain = true;
            throw;
        }
    }

    internal void CloseOpened(List<Exception> failures)
    {
        if (opened is null || closeAttempted)
        {
            return;
        }
        closeAttempted = true;
        var closed = false;
        ServerFailureObserver.Observe(() =>
        {
            opened.Dispose();
            closed = true;
        }, failures);
        if (closed)
        {
            opened = null;
        }
        else
        {
            uncertain = true;
        }
    }

    internal StoreIdentity ReadTargetIdentity()
    {
        var policy = Source.StorageExecution.Value;
        return ZoneTreeIdentityFile.Read(Path.Combine(Target, P.Identity), policy.MaximumIdentityFileBytes, policy.StreamBufferBytes);
    }

    internal void Cleanup(List<Exception> failures)
    {
        CloseOpened(failures);
        CloseSource(failures);
        if (CanOpen && ChildClosed)
        {
            ServerFailureObserver.Observe(() => Directory.Delete(Root, recursive: true), failures);
        }
    }
}
