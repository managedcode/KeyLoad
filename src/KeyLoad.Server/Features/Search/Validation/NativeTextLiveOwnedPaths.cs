using Microsoft.Extensions.Options;
namespace KeyLoad.Server.Features.Search;

internal static class NativeTextLiveOwnedPaths
{
    private const string NativeFilePrefix = NativeTextProtocol.NativeDirectory + "/";

    internal static void VerifyFilesystem(string generationPath, NativeTextOwnedPath[] ownedPaths, NativeTextOwnedPath[] expectedPaths, IOptions<NativeTextExecutionOptions> executionOptions)
    {
        const string AllEntriesSearchPattern = "*";
        const int EmptyAttributesFileAttributesReparsePoint = 0;
        const int EmptyAttributesFileAttributesDirectory = 0;
        const char SlashCharacter = '/';
        const int RelativeCountStep = 1;

        NativeTextValidation.ValidateOwnedPaths(ownedPaths, executionOptions: executionOptions);
        var nativePath = Path.Combine(generationPath, NativeTextProtocol.NativeDirectory);
        NativeTextFileIO.VerifyDirectory(nativePath);
        var actual = new SortedDictionary<string, bool>(StringComparer.Ordinal)
        {
            [NativeTextProtocol.NativeDirectory] = true
        };
        foreach (var path in Directory.EnumerateFileSystemEntries(nativePath, AllEntriesSearchPattern, SearchOption.AllDirectories))
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != EmptyAttributesFileAttributesReparsePoint)
            {
                throw NativeTextErrors.Ownership();
            }
            var relative = NativeTextPath.RelativePath(generationPath, path);
            var isDirectory = (attributes & FileAttributes.Directory) != EmptyAttributesFileAttributesDirectory;
            if (relative.Count(static character => character == SlashCharacter) + RelativeCountStep > executionOptions.Value.MaximumDepth)
            {
                throw NativeTextErrors.BoundExceeded();
            }
            if (!IsOwned(ownedPaths, relative, isDirectory))
            {
                throw NativeTextErrors.Ownership();
            }
            if (actual.Count == executionOptions.Value.MaximumEntries)
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

    internal static NativeTextOwnedPath[] FromFiles(NativeTextFile[] files, IOptions<NativeTextExecutionOptions> executionOptions)
    {
        var paths = new SortedDictionary<string, bool>(StringComparer.Ordinal)
        {
            [NativeTextProtocol.NativeDirectory] = true
        };
        foreach (var file in files)
        {
            AddFile(paths, file.RelativePath, executionOptions: executionOptions);
        }
        if (paths.Count > executionOptions.Value.MaximumEntries)
        {
            throw NativeTextErrors.BoundExceeded();
        }
        var result = paths.Select(static path => new NativeTextOwnedPath(path.Key, path.Value)).ToArray();
        NativeTextValidation.ValidateOwnedPaths(result, executionOptions: executionOptions);
        return result;
    }

    private static void AddFile(SortedDictionary<string, bool> paths, string relativePath, IOptions<NativeTextExecutionOptions> executionOptions)
    {
        const char SlashCharacter = '/';
        const int RelativePathCountStep = 1;

        if (string.IsNullOrWhiteSpace(relativePath)
            || !relativePath.StartsWith(NativeFilePrefix, StringComparison.Ordinal)
            || paths.ContainsKey(relativePath))
        {
            throw NativeTextErrors.Corrupt();
        }
        if (relativePath.Count(static character => character == SlashCharacter) + RelativePathCountStep > executionOptions.Value.MaximumDepth
            || paths.Count == executionOptions.Value.MaximumEntries)
        {
            throw NativeTextErrors.BoundExceeded();
        }
        paths.Add(relativePath, false);
        AddParentDirectories(paths, relativePath, executionOptions: executionOptions);
    }

    private static void AddParentDirectories(SortedDictionary<string, bool> paths, string relativePath, IOptions<NativeTextExecutionOptions> executionOptions)
    {
        const char SlashCharacter = '/';

        var parentEnd = relativePath.LastIndexOf(SlashCharacter);
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
                if (paths.Count == executionOptions.Value.MaximumEntries)
                {
                    throw NativeTextErrors.BoundExceeded();
                }
                paths.Add(parent, true);
            }
            parentEnd = parent.LastIndexOf(SlashCharacter);
        }
    }
}
