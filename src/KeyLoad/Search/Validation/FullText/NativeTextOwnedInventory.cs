using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextOwnedInventory
{
    internal static void ValidateTrackedLayout(string generationPath, NativeTextOwnedPath[] ownedPaths, ReadExecutionBudget? budget, bool allowMissingNative, IOptions<NativeTextExecutionOptions> executionOptions)
    {
        const int EntriesInitialValue = 1;
        const int DirectoriesInitialValue = 1;
        const string AllEntriesSearchPattern = "*";

        NativeTextValidation.ValidateOwnedPaths(ownedPaths, executionOptions: executionOptions);
        var nativePath = Path.Combine(generationPath, NativeTextProtocol.NativeDirectory);
        if (!Directory.Exists(nativePath))
        {
            VerifyMissingNative(nativePath, allowMissingNative);
            return;
        }
        NativeTextFileIO.VerifyDirectory(nativePath);
        var counts = new LayoutCounts { Entries = EntriesInitialValue, Directories = DirectoriesInitialValue };
        foreach (var path in Directory.EnumerateFileSystemEntries(nativePath, AllEntriesSearchPattern, SearchOption.AllDirectories))
        {
            budget?.Check();
            var attributes = File.GetAttributes(path);
            VerifyEntry(generationPath, path, attributes, ownedPaths, counts, executionOptions: executionOptions);
        }
    }

    internal static NativeTextFile[] CaptureFiles(string generationPath, NativeTextOwnedPath[] ownedPaths, ReadExecutionBudget? budget, IOptions<NativeTextExecutionOptions> executionOptions, NativeTextOnlineGenerationPin? retained = null)
        => new NativeTextInventoryCapture(generationPath, ownedPaths, budget, executionOptions: executionOptions, retained: retained).Capture();

    internal static void RequireOwnedPath(NativeTextOwnedPath[] ownedPaths, string relative, bool directory)
    {
        if (!ownedPaths.Any(owned => owned.RelativePath == relative && owned.IsDirectory == directory))
        {
            throw NativeTextErrors.Ownership();
        }
    }

    private static void VerifyMissingNative(string path, bool allowMissing)
    {
        const int EmptyAttributesFileAttributesReparsePoint = 0;
        const int EmptyAttributesFileAttributesDirectory = 0;

        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != EmptyAttributesFileAttributesReparsePoint || (attributes & FileAttributes.Directory) == EmptyAttributesFileAttributesDirectory)
            {
                throw NativeTextErrors.Ownership();
            }
        }
        catch (FileNotFoundException)
        {
            if (allowMissing)
            {
                return;
            }
            throw NativeTextErrors.Ownership();
        }
        catch (DirectoryNotFoundException)
        {
            if (allowMissing)
            {
                return;
            }
            throw NativeTextErrors.Ownership();
        }
        throw NativeTextErrors.Ownership();
    }

    private static void VerifyEntry(string generationPath, string path, FileAttributes attributes, NativeTextOwnedPath[] ownedPaths, LayoutCounts counts, IOptions<NativeTextExecutionOptions> executionOptions)
    {
        const int EmptyAttributesFileAttributesReparsePoint = 0;
        const int EmptyAttributesFileAttributesDirectory = 0;
        const char SlashCharacter = '/';
        const int RelativeCountStep = 1;

        if (++counts.Entries > executionOptions.Value.MaximumEntries)
        {
            throw NativeTextErrors.BoundExceeded();
        }
        if ((attributes & FileAttributes.ReparsePoint) != EmptyAttributesFileAttributesReparsePoint)
        {
            throw NativeTextErrors.Ownership();
        }
        var relative = NativeTextPath.RelativePath(generationPath, path);
        var directory = (attributes & FileAttributes.Directory) != EmptyAttributesFileAttributesDirectory;
        RequireOwnedPath(ownedPaths, relative, directory);
        if (relative.Count(character => character == SlashCharacter) + RelativeCountStep > executionOptions.Value.MaximumDepth)
        {
            throw NativeTextErrors.BoundExceeded();
        }
        if (directory)
        {
            if (++counts.Directories > executionOptions.Value.MaximumDirectories)
            {
                throw NativeTextErrors.BoundExceeded();
            }
            return;
        }
        var length = new FileInfo(path).Length;
        if (++counts.Files > executionOptions.Value.MaximumFiles
            || length > executionOptions.Value.MaximumDiskBytes - counts.Bytes)
        {
            throw NativeTextErrors.BoundExceeded();
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
