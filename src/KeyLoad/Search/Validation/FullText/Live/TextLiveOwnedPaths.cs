using Microsoft.Extensions.Options;
namespace KeyLoad.Server.Search;

internal static class TextLiveOwnedPaths
{
    private const string RuntimeFilePrefix = TextProtocol.RuntimeDirectoryCore + "/";

    internal static void VerifyFilesystem(string generationPath, TextOwnedPath[] ownedPaths, TextOwnedPath[] expectedPaths, IOptions<TextExecutionOptions> executionOptions)
    {
        const string AllEntriesSearchPattern = "*";
        const int EmptyAttributesFileAttributesReparsePoint = 0;
        const int EmptyAttributesFileAttributesDirectory = 0;
        const char SlashCharacter = '/';
        const int RelativeCountStep = 1;

        TextValidation.ValidateOwnedPaths(ownedPaths, executionOptions: executionOptions);
        var runtimePath = Path.Combine(generationPath, TextProtocol.RuntimeDirectoryCore);
        TextFileIO.VerifyDirectory(runtimePath);
        var actual = new SortedDictionary<string, bool>(StringComparer.Ordinal)
        {
            [TextProtocol.RuntimeDirectoryCore] = true
        };
        foreach (var path in Directory.EnumerateFileSystemEntries(runtimePath, AllEntriesSearchPattern, SearchOption.AllDirectories))
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != EmptyAttributesFileAttributesReparsePoint)
            {
                throw TextErrors.Ownership();
            }
            var relative = RuntimeTextPath.RelativePath(generationPath, path);
            var isDirectory = (attributes & FileAttributes.Directory) != EmptyAttributesFileAttributesDirectory;
            if (relative.Count(static character => character == SlashCharacter) + RelativeCountStep > executionOptions.Value.MaximumDepth)
            {
                throw TextErrors.BoundExceeded();
            }
            if (!IsOwned(ownedPaths, relative, isDirectory))
            {
                throw TextErrors.Ownership();
            }
            if (actual.Count == executionOptions.Value.MaximumEntries)
            {
                throw TextErrors.BoundExceeded();
            }
            if (!actual.TryAdd(relative, isDirectory))
            {
                throw TextErrors.Ownership();
            }
        }
        var actualPaths = actual.Select(static path => new TextOwnedPath(path.Key, path.Value));
        if (!actualPaths.SequenceEqual(expectedPaths))
        {
            throw TextErrors.Corrupt();
        }
    }

    private static bool IsOwned(TextOwnedPath[] ownedPaths, string relative, bool isDirectory)
    {
        const int LowInitialValue = 0;
        const int OwnedPathsLengthStep = 1;
        const int BinarySearchDivisor = 2;
        const int EmptyComparison = 0;
        const int ComparisonValidationBoundary = 0;
        const int MiddleStep = 1;

        var low = LowInitialValue;
        var high = ownedPaths.Length - OwnedPathsLengthStep;
        while (low <= high)
        {
            var middle = low + ((high - low) / BinarySearchDivisor);
            var comparison = StringComparer.Ordinal.Compare(ownedPaths[middle].RelativePath, relative);
            if (comparison == EmptyComparison)
            {
                return ownedPaths[middle].IsDirectory == isDirectory;
            }
            if (comparison < ComparisonValidationBoundary)
            {
                low = middle + MiddleStep;
            }
            else
            {
                high = middle - MiddleStep;
            }
        }
        return false;
    }

    internal static TextOwnedPath[] FromFiles(TextFile[] files, IOptions<TextExecutionOptions> executionOptions)
    {
        var paths = new SortedDictionary<string, bool>(StringComparer.Ordinal)
        {
            [TextProtocol.RuntimeDirectoryCore] = true
        };
        foreach (var file in files)
        {
            AddFile(paths, file.RelativePath, executionOptions: executionOptions);
        }
        if (paths.Count > executionOptions.Value.MaximumEntries)
        {
            throw TextErrors.BoundExceeded();
        }
        var result = paths.Select(static path => new TextOwnedPath(path.Key, path.Value)).ToArray();
        TextValidation.ValidateOwnedPaths(result, executionOptions: executionOptions);
        return result;
    }

    private static void AddFile(SortedDictionary<string, bool> paths, string relativePath, IOptions<TextExecutionOptions> executionOptions)
    {
        const char SlashCharacter = '/';
        const int RelativePathCountStep = 1;

        if (string.IsNullOrWhiteSpace(relativePath)
            || !relativePath.StartsWith(RuntimeFilePrefix, StringComparison.Ordinal)
            || paths.ContainsKey(relativePath))
        {
            throw TextErrors.Corrupt();
        }
        if (relativePath.Count(static character => character == SlashCharacter) + RelativePathCountStep > executionOptions.Value.MaximumDepth
            || paths.Count == executionOptions.Value.MaximumEntries)
        {
            throw TextErrors.BoundExceeded();
        }
        paths.Add(relativePath, false);
        AddParentDirectories(paths, relativePath, executionOptions: executionOptions);
    }

    private static void AddParentDirectories(SortedDictionary<string, bool> paths, string relativePath, IOptions<TextExecutionOptions> executionOptions)
    {
        const char SlashCharacter = '/';

        var parentEnd = relativePath.LastIndexOf(SlashCharacter);
        while (parentEnd >= TextProtocol.RuntimeDirectoryCore.Length)
        {
            var parent = relativePath[..parentEnd];
            if (paths.TryGetValue(parent, out var isDirectory))
            {
                if (!isDirectory)
                {
                    throw TextErrors.Corrupt();
                }
            }
            else
            {
                if (paths.Count == executionOptions.Value.MaximumEntries)
                {
                    throw TextErrors.BoundExceeded();
                }
                paths.Add(parent, true);
            }
            parentEnd = parent.LastIndexOf(SlashCharacter);
        }
    }
}
