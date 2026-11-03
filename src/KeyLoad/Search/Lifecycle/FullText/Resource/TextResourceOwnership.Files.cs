using KeyLoad.Core;

namespace KeyLoad.Server.Search;

internal sealed partial class TextResourceOwnership
{
    internal void CheckPhysical(ReadExecutionBudget? budget)
    {
        lock (gate)
        {
            Check(budget);
        }
    }

    internal TextOpenFileGroup OpenFile(string path, ZoneTree.AbstractFileStream.IFileStream stream)
        => TextOwnedFileMutation.Open(gate, openFiles, path, stream);

    internal void CloseOwnedFile(TextOpenFileGroup group, ZoneTree.AbstractFileStream.IFileStream stream)
        => TextOwnedFileMutation.CloseOwnedFile(gate, openFiles, () => Check(null), group, stream);

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
