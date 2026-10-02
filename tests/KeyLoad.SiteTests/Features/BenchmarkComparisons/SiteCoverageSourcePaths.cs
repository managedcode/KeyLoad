namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteCoverageSourcePaths
{
    public static IEnumerable<string> EnumerateFilesWithoutLinks(string directory)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.Ordinal))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(SiteCoverageTokens.InvalidSourceFailure);
            }

            if ((attributes & FileAttributes.Directory) != 0)
            {
                foreach (var child in EnumerateFilesWithoutLinks(entry))
                {
                    yield return child;
                }
            }
            else
            {
                yield return entry;
            }
        }
    }

    public static string ResolveSourcePath(string repository, string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(repository, relativePath.Replace(
            SiteCoverageTokens.RelativeSeparator, Path.DirectorySeparatorChar)));
        var root = Path.GetFullPath(repository) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(root, PathComparison) || !File.Exists(fullPath) || !PathHasNoLinks(repository, fullPath))
        {
            throw new InvalidOperationException(SiteCoverageTokens.InvalidSourceFailure);
        }

        return fullPath;
    }

    public static bool PathHasNoLinks(string root, string path)
    {
        var current = Path.GetFullPath(root);
        if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
        {
            return false;
        }

        var relative = Path.GetRelativePath(current, Path.GetFullPath(path));
        if (Path.IsPathRooted(relative) || relative.StartsWith(SiteCoverageTokens.ParentSegmentMarker,
                StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            current = Path.Combine(current, segment);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                return false;
            }
        }

        return true;
    }

    public static bool PathsEqual(string left, string right) => string.Equals(Path.GetFullPath(left),
        Path.GetFullPath(right), PathComparison);

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
