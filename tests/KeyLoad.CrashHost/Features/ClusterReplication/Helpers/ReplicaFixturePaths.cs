namespace KeyLoad.CrashHost;

/// <summary>Creates physical test roots while preserving strict production rejection of reparse paths.</summary>
internal static class ReplicaFixturePaths
{
    private const string GuidFormat = "N";

    /// <summary>Resolves existing system temp aliases, including macOS /var, before creating a private test directory.</summary>
    /// <param name="prefix">The owning fixture's named directory prefix.</param>
    public static string NewDirectory(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        return Path.Combine(Resolve(new(Path.GetTempPath())), prefix + Guid.NewGuid().ToString(GuidFormat));
    }

    private static string Resolve(DirectoryInfo directory)
    {
        if (directory.Parent is null)
        {
            return directory.FullName;
        }
        var entry = new DirectoryInfo(Path.Combine(Resolve(directory.Parent), directory.Name));
        return entry.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? entry.FullName;
    }
}
