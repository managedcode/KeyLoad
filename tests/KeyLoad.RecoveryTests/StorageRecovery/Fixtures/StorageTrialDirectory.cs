namespace KeyLoad.RecoveryTests.Features.StorageRecovery;

internal static class StorageTrialDirectory
{
    internal static string ResolveExistingDirectory(DirectoryInfo directory)
    {
        if (directory.Parent is null)
        {
            return directory.FullName;
        }
        var entry = new DirectoryInfo(Path.Combine(ResolveExistingDirectory(directory.Parent), directory.Name));
        return entry.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? entry.FullName;
    }
}
