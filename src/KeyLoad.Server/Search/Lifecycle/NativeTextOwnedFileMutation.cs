namespace KeyLoad.Server.Features.Search;

internal static class NativeTextOwnedFileMutation
{
    private const int NoPhysicalFailures = 0;

    internal static void DeleteOwnedFile(Lock gate, NativeTextOpenFileGroups openFiles, Action check, string path, Action mutation)
    {
        lock (gate)
        {
            check();
            var failures = new List<Exception>();
            ServerFailureObserver.Observe(() =>
            {
                mutation();
                openFiles.Detach(path);
            }, failures);
            if (failures.Count != NoPhysicalFailures)
            {
                ServerFailureObserver.Observe(() => openFiles.MarkAmbiguous(path), failures);
            }
            ServerFailureObserver.Observe(() => check(), failures);
            ServerFailureObserver.ThrowIfAny(failures);
        }
    }

    internal static void ReplaceOwnedFiles(Lock gate, NativeTextOpenFileGroups openFiles, Action check, string source, string destination, string? backup, Action mutation)
    {
        lock (gate)
        {
            check();
            var failures = new List<Exception>();
            ServerFailureObserver.Observe(() =>
            {
                mutation();
                openFiles.Replace(source, destination, backup);
            }, failures);
            if (failures.Count != NoPhysicalFailures)
            {
                ServerFailureObserver.Observe(() => openFiles.MarkAmbiguous(source, destination, backup), failures);
            }
            ServerFailureObserver.Observe(() => check(), failures);
            ServerFailureObserver.ThrowIfAny(failures);
        }
    }

    internal static NativeTextOpenFileGroup Open(Lock gate, NativeTextOpenFileGroups openFiles, string path, ZoneTree.AbstractFileStream.IFileStream stream)
    {
        lock (gate)
        {
            return openFiles.Open(Path.GetFullPath(path), stream);
        }
    }
}
