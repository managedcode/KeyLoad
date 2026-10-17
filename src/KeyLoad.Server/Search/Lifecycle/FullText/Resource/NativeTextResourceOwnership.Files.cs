namespace KeyLoad.Server.Features.Search;

internal sealed partial class NativeTextResourceOwnership
{

    internal NativeTextOpenFileGroup OpenFile(string path, ZoneTree.AbstractFileStream.IFileStream stream)
        => NativeTextOwnedFileMutation.Open(gate, openFiles, path, stream);

    internal void BeginCloseFile(NativeTextOpenFileGroup group, ZoneTree.AbstractFileStream.IFileStream stream)
    {
        lock (gate)
        {
            openFiles.BeginClose(group, stream);
        }
    }

    internal void CloseFile(NativeTextOpenFileGroup group, ZoneTree.AbstractFileStream.IFileStream stream)
    {
        lock (gate)
        {
            openFiles.Close(group, stream);
            Check(null);
        }
    }

    internal void RequireKnownFile(string path)
    {
        lock (gate)
        {
            openFiles.RequireKnownPath(Path.GetFullPath(path));
        }
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
        => NativeTextOwnedFileMutation.DeleteOwnedFile(gate, openFiles, () => Check(null), path, mutation);

    internal void ReplaceOwnedFiles(string source, string destination, string? backup, Action mutation)
        => NativeTextOwnedFileMutation.ReplaceOwnedFiles(gate, openFiles, () => Check(null), source, destination, backup, mutation);

}
