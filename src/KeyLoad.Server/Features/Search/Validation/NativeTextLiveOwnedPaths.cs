namespace KeyLoad.Server.Features.Search;

internal static class NativeTextLiveOwnedPaths
{
    private const string NativeFilePrefix = NativeTextProtocol.NativeDirectory + "/";

    internal static void VerifyFilesystem(string generationPath, NativeTextOwnedPath[] ownedPaths,
        NativeTextOwnedPath[] expectedPaths)
    {
        NativeTextValidation.ValidateOwnedPaths(ownedPaths);
        var nativePath = Path.Combine(generationPath, NativeTextProtocol.NativeDirectory);
        NativeTextFileIO.VerifyDirectory(nativePath);
        var actual = new SortedDictionary<string, bool>(StringComparer.Ordinal)
        {
            [NativeTextProtocol.NativeDirectory] = true
        };
        foreach (var path in Directory.EnumerateFileSystemEntries(nativePath, "*", SearchOption.AllDirectories))
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw NativeTextErrors.Ownership();
            }
            var relative = NativeTextPath.RelativePath(generationPath, path);
            var isDirectory = (attributes & FileAttributes.Directory) != 0;
            if (relative.Count(static character => character == '/') + 1 > NativeTextProtocol.MaximumDepth)
            {
                throw NativeTextErrors.BoundExceeded();
            }
            if (!IsOwned(ownedPaths, relative, isDirectory))
            {
                throw NativeTextErrors.Ownership();
            }
            if (actual.Count == NativeTextProtocol.MaximumEntries)
            {
                throw NativeTextErrors.BoundExceeded();
            }
            if (!actual.TryAdd(relative, isDirectory))
            {
                throw NativeTextErrors.Ownership();
            }
        }
        var actualPaths = actual.Select(static path => new NativeTextOwnedPath(path.Key, path.Value));
        if (!actualPaths.SequenceEqual(expectedPaths))
        {
            throw NativeTextErrors.Corrupt();
        }
    }

    private static bool IsOwned(NativeTextOwnedPath[] ownedPaths, string relative, bool isDirectory)
    {
        var low = 0;
        var high = ownedPaths.Length - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var comparison = StringComparer.Ordinal.Compare(ownedPaths[middle].RelativePath, relative);
            if (comparison == 0)
            {
                return ownedPaths[middle].IsDirectory == isDirectory;
            }
            if (comparison < 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }
        return false;
    }

    internal static NativeTextOwnedPath[] FromFiles(NativeTextFile[] files)
    {
        var paths = new SortedDictionary<string, bool>(StringComparer.Ordinal)
        {
            [NativeTextProtocol.NativeDirectory] = true
        };
        foreach (var file in files)
        {
            AddFile(paths, file.RelativePath);
        }
        if (paths.Count > NativeTextProtocol.MaximumEntries)
        {
            throw NativeTextErrors.BoundExceeded();
        }
        var result = paths.Select(static path => new NativeTextOwnedPath(path.Key, path.Value)).ToArray();
        NativeTextValidation.ValidateOwnedPaths(result);
        return result;
    }

    private static void AddFile(SortedDictionary<string, bool> paths, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)
            || !relativePath.StartsWith(NativeFilePrefix, StringComparison.Ordinal)
            || paths.ContainsKey(relativePath))
        {
            throw NativeTextErrors.Corrupt();
        }
        if (relativePath.Count(static character => character == '/') + 1 > NativeTextProtocol.MaximumDepth
            || paths.Count == NativeTextProtocol.MaximumEntries)
        {
            throw NativeTextErrors.BoundExceeded();
        }
        paths.Add(relativePath, false);
        AddParentDirectories(paths, relativePath);
    }

    private static void AddParentDirectories(SortedDictionary<string, bool> paths, string relativePath)
    {
        var parentEnd = relativePath.LastIndexOf('/');
        while (parentEnd >= NativeTextProtocol.NativeDirectory.Length)
        {
            var parent = relativePath[..parentEnd];
            if (paths.TryGetValue(parent, out var isDirectory))
            {
                if (!isDirectory)
                {
                    throw NativeTextErrors.Corrupt();
                }
            }
            else
            {
                if (paths.Count == NativeTextProtocol.MaximumEntries)
                {
                    throw NativeTextErrors.BoundExceeded();
                }
                paths.Add(parent, true);
            }
            parentEnd = parent.LastIndexOf('/');
        }
    }
}
