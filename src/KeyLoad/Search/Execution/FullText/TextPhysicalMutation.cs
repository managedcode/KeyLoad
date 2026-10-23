namespace KeyLoad.Server.Features.Search;

internal static class TextPhysicalMutation
{
    internal static void DeleteDirectory(TextOpenFileGroups openFiles, string path,
        Action mutation, List<Exception> failures)
    {
        ServerFailureObserver.Observe(() =>
        {
            mutation();
            openFiles.DetachDirectory(path, ambiguous: false);
        }, failures);
        if (failures.Count != 0)
        {
            ServerFailureObserver.Observe(
                () => openFiles.DetachDirectory(path, ambiguous: true), failures);
        }
    }
}
