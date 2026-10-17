namespace KeyLoad.Server.Features.Search;

internal static class NativeTextPhysicalMutation
{
    private const int NoPhysicalFailures = 0;

    internal static void DeleteDirectory(NativeTextOpenFileGroups openFiles, string path,
        Action mutation, List<Exception> failures)
    {
        ServerFailureObserver.Observe(() =>
        {
            mutation();
            openFiles.DetachDirectory(path, ambiguous: false);
        }, failures);
        if (failures.Count != NoPhysicalFailures)
        {
            ServerFailureObserver.Observe(
                () => openFiles.DetachDirectory(path, ambiguous: true), failures);
        }
    }
}
