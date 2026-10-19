namespace KeyLoad.AppHost.Hosting;

/// <summary>Resolves the configured data-root aliases before assigning storage paths to server processes.</summary>
internal static class ClusterDataDirectory
{
    internal static string Resolve(string path)
    {
        var full = Path.GetFullPath(path);
        var current = Path.GetPathRoot(full)!;
        foreach (var segment in full[current.Length..].Split(Path.DirectorySeparatorChar,
            StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (Directory.Exists(current)
                && new DirectoryInfo(current).ResolveLinkTarget(returnFinalTarget: true) is { } target)
            {
                current = Resolve(target.FullName);
            }
        }
        return current;
    }
}
