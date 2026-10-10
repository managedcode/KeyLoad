namespace KeyLoad.Server.Features.Search;

internal sealed partial class NativeTextResourceOwnership
{
    private const int NoPhysicalFailures = 0;

    internal NativeTextOpenFileGroup OpenFile(string path, ZoneTree.AbstractFileStream.IFileStream stream)
    {
        lock (gate)
        { return openFiles.Open(Path.GetFullPath(path), stream); }
    }

    internal void BeginCloseFile(NativeTextOpenFileGroup group, ZoneTree.AbstractFileStream.IFileStream stream)
    {
        lock (gate)
        { openFiles.BeginClose(group, stream); }
    }

    internal void CloseFile(NativeTextOpenFileGroup group, ZoneTree.AbstractFileStream.IFileStream stream)
    {
        lock (gate)
        { openFiles.Close(group, stream); Check(null); }
    }

    internal void RequireKnownFile(string path)
    {
        lock (gate)
        { openFiles.RequireKnownPath(Path.GetFullPath(path)); }
    }

    internal void DeleteOwnedDirectory(string path, Action mutation)
    {
        lock (gate)
        {
            Check(null);
            var failures = new List<Exception>();
            NativeTextPhysicalMutation.DeleteDirectory(openFiles, path, mutation, failures);
            ServerFailureObserver.Observe(() => Check(null), failures);
            ServerFailureObserver.ThrowIfAny(failures);
        }
    }

    internal void DeleteOwnedFile(string path, Action mutation)
    {
        lock (gate)
        {
            Check(null);
            var failures = new List<Exception>();
            ServerFailureObserver.Observe(() => { mutation(); openFiles.Detach(path); }, failures);
            if (failures.Count != NoPhysicalFailures)
            { ServerFailureObserver.Observe(() => openFiles.MarkAmbiguous(path), failures); }
            ServerFailureObserver.Observe(() => Check(null), failures);
            ServerFailureObserver.ThrowIfAny(failures);
        }
    }

    internal void ReplaceOwnedFiles(string source, string destination, string? backup, Action mutation)
    {
        lock (gate)
        {
            Check(null);
            var failures = new List<Exception>();
            ServerFailureObserver.Observe(() => { mutation(); openFiles.Replace(source, destination, backup); }, failures);
            if (failures.Count != NoPhysicalFailures)
            { ServerFailureObserver.Observe(() => openFiles.MarkAmbiguous(source, destination, backup), failures); }
            ServerFailureObserver.Observe(() => Check(null), failures);
            ServerFailureObserver.ThrowIfAny(failures);
        }
    }

}
