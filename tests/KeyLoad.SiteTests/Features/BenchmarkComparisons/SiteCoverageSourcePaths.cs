namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteCoverageSourcePaths
{
    internal static IEnumerable<string> EnumerateModules(string repository, string prefix, string? filePrefix = null)
    {
        var directory = Path.Combine(repository, prefix.Replace(SiteCoverageTokens.RelativeSeparator,
            Path.DirectorySeparatorChar));
        if (!Directory.Exists(directory) || !PathHasNoLinks(repository, directory))
        {
            throw new InvalidOperationException(SiteCoverageTokens.InventoryFailure);
        }

        foreach (var file in EnumerateFilesWithoutLinks(directory))
        {
            var relative = Path.GetRelativePath(repository, file).Replace(Path.DirectorySeparatorChar,
                SiteCoverageTokens.RelativeSeparator);
            if (Path.GetExtension(file) == SiteCoverageTokens.ModuleExtension &&
                !relative.StartsWith(SiteCoverageTokens.VendorSourcePrefix, StringComparison.Ordinal) &&
                (filePrefix is null || Path.GetFileName(file).StartsWith(filePrefix, StringComparison.Ordinal)))
            {
                yield return relative;
            }
        }
    }

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
