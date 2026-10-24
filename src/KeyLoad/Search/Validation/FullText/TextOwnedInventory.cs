using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Search;

internal static class TextOwnedInventory
{
    internal static void ValidateTrackedLayout(string generationPath, TextOwnedPath[] ownedPaths, ReadExecutionBudget? budget, bool runtimeAllowMissing, IOptions<TextExecutionOptions> executionOptions)
    {
        const int EntriesInitialValue = 1;
        const int DirectoriesInitialValue = 1;
        const string AllEntriesSearchPattern = "*";

        TextValidation.ValidateOwnedPaths(ownedPaths, executionOptions: executionOptions);
        var runtimePath = Path.Combine(generationPath, TextProtocol.RuntimeDirectoryCore);
        if (!Directory.Exists(runtimePath))
        {
            VerifyMissing(runtimePath, runtimeAllowMissing);
            return;
        }
        TextFileIO.VerifyDirectory(runtimePath);
        var counts = new LayoutCounts { Entries = EntriesInitialValue, Directories = DirectoriesInitialValue };
        foreach (var path in Directory.EnumerateFileSystemEntries(runtimePath, AllEntriesSearchPattern, SearchOption.AllDirectories))
        {
            budget?.Check();
            var attributes = File.GetAttributes(path);
            VerifyEntry(generationPath, path, attributes, ownedPaths, counts, executionOptions: executionOptions);
        }
    }

    internal static TextFile[] CaptureFiles(string generationPath, TextOwnedPath[] ownedPaths, ReadExecutionBudget? budget, IOptions<TextExecutionOptions> executionOptions, TextOnlineGenerationPin? retained = null)
        => new TextInventoryCapture(generationPath, ownedPaths, budget, executionOptions: executionOptions, retained: retained).Capture();

    internal static void RequireOwnedPath(TextOwnedPath[] ownedPaths, string relative, bool directory)
    {
        if (!ownedPaths.Any(owned => owned.RelativePath == relative && owned.IsDirectory == directory))
        {
            throw TextErrors.Ownership();
        }
    }

    private static void VerifyMissing(string path, bool allowMissing)
    {
        const int EmptyAttributesFileAttributesReparsePoint = 0;
        const int EmptyAttributesFileAttributesDirectory = 0;

        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != EmptyAttributesFileAttributesReparsePoint || (attributes & FileAttributes.Directory) == EmptyAttributesFileAttributesDirectory)
            {
                throw TextErrors.Ownership();
            }
        }
        catch (FileNotFoundException)
        {
            if (allowMissing)
            {
                return;
            }
            throw TextErrors.Ownership();
        }
        catch (DirectoryNotFoundException)
        {
            if (allowMissing)
            {
                return;
            }
            throw TextErrors.Ownership();
        }
        throw TextErrors.Ownership();
    }

    private static void VerifyEntry(string generationPath, string path, FileAttributes attributes, TextOwnedPath[] ownedPaths, LayoutCounts counts, IOptions<TextExecutionOptions> executionOptions)
    {
        const int EmptyAttributesFileAttributesReparsePoint = 0;
        const int EmptyAttributesFileAttributesDirectory = 0;
        const char SlashCharacter = '/';
        const int RelativeCountStep = 1;

        if (++counts.Entries > executionOptions.Value.MaximumEntries)
        {
            throw TextErrors.BoundExceeded();
        }
        if ((attributes & FileAttributes.ReparsePoint) != EmptyAttributesFileAttributesReparsePoint)
        {
            throw TextErrors.Ownership();
        }
        var relative = RuntimeTextPath.RelativePath(generationPath, path);
        var directory = (attributes & FileAttributes.Directory) != EmptyAttributesFileAttributesDirectory;
        RequireOwnedPath(ownedPaths, relative, directory);
        if (relative.Count(character => character == SlashCharacter) + RelativeCountStep > executionOptions.Value.MaximumDepth)
        {
            throw TextErrors.BoundExceeded();
        }
        if (directory)
        {
            if (++counts.Directories > executionOptions.Value.MaximumDirectories)
            {
                throw TextErrors.BoundExceeded();
            }
            return;
        }
        var length = new FileInfo(path).Length;
        if (++counts.Files > executionOptions.Value.MaximumFiles
            || length > executionOptions.Value.MaximumDiskBytes - counts.Bytes)
        {
            throw TextErrors.BoundExceeded();
        }
        counts.Bytes += length;
    }

    private sealed class LayoutCounts
    {
        internal int Entries
        {
            get; set;
        }
        internal int Files
        {
            get; set;
        }
        internal int Directories
        {
            get; set;
        }
        internal long Bytes
        {
            get; set;
        }
    }
}
