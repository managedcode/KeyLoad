namespace KeyLoad.Server.Search;

internal sealed partial class TextResourceOwnership
{

    internal TextOpenFileGroup OpenFile(string path, ZoneTree.AbstractFileStream.IFileStream stream)
        => TextOwnedFileMutation.Open(gate, openFiles, path, stream);

    internal void BeginCloseFile(TextOpenFileGroup group, ZoneTree.AbstractFileStream.IFileStream stream)
    {
        lock (gate)
        {
            openFiles.BeginClose(group, stream);
        }
    }

    internal void CloseFile(TextOpenFileGroup group, ZoneTree.AbstractFileStream.IFileStream stream)
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
            TextPhysicalMutation.DeleteDirectory(openFiles, path, mutation, failures);
            ServerFailureObserver.Observe(() => Check(null), failures);
            ServerFailureObserver.ThrowIfAny(failures);
        }
    }

    internal void DeleteOwnedFile(string path, Action mutation)
        => TextOwnedFileMutation.DeleteOwnedFile(gate, openFiles, () => Check(null), path, mutation);

    internal void ReplaceOwnedFiles(string source, string destination, string? backup, Action mutation)
        => TextOwnedFileMutation.ReplaceOwnedFiles(gate, openFiles, () => Check(null), source, destination, backup, mutation);

}
