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
        internal int Entries { get; set; }
        internal int Files { get; set; }
        internal int Directories { get; set; }
        internal long Bytes { get; set; }
    }
}

internal sealed class NativeTextInventoryCapture
{
    private const int EntriesInitialValue = 1;
    private const int DirectoriesInitialValue = 1;

    private readonly IOptions<NativeTextExecutionOptions> executionOptions;

    private readonly string _generationPath;
    private readonly NativeTextOwnedPath[] _ownedPaths;
    private readonly ReadExecutionBudget? _budget;
    private readonly NativeTextOnlineGenerationPin? retained;
    private readonly List<NativeTextFile> _files = [];
    private int _entries = EntriesInitialValue;
    private int _directories = DirectoriesInitialValue;
    private long _bytes;

    internal NativeTextInventoryCapture(string generationPath, NativeTextOwnedPath[] ownedPaths, ReadExecutionBudget? budget, IOptions<NativeTextExecutionOptions> executionOptions, NativeTextOnlineGenerationPin? retained = null)
    {
        this.executionOptions = executionOptions;
        _generationPath = generationPath;
        _ownedPaths = ownedPaths;
        _budget = budget;
        this.retained = retained;
    }

    internal NativeTextFile[] Capture()
    {
        const string AllEntriesSearchPattern = "*";

        retained?.RequirePath(_generationPath);
        var nativePath = Path.Combine(_generationPath, NativeTextProtocol.NativeDirectory);
        foreach (var path in Directory.EnumerateFileSystemEntries(nativePath, AllEntriesSearchPattern, SearchOption.AllDirectories))
        {
            _budget?.Check();
            CaptureEntry(path, File.GetAttributes(path));
        }
        retained?.RequirePath(_generationPath);
        _files.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.RelativePath, right.RelativePath));
        return _files.ToArray();
    }

    private void CaptureEntry(string path, FileAttributes attributes)
    {
        const int EmptyAttributesFileAttributesReparsePoint = 0;
        const int EmptyAttributesFileAttributesDirectory = 0;
        const char SlashCharacter = '/';
        const int RelativeCountStep = 1;

        if (++_entries > executionOptions.Value.MaximumEntries)
        {
            throw NativeTextErrors.BoundExceeded();
        }
        if ((attributes & FileAttributes.ReparsePoint) != EmptyAttributesFileAttributesReparsePoint)
        {
            throw NativeTextErrors.Ownership();
        }
        var relative = NativeTextPath.RelativePath(_generationPath, path);
        var isDirectory = (attributes & FileAttributes.Directory) != EmptyAttributesFileAttributesDirectory;
        NativeTextOwnedInventory.RequireOwnedPath(_ownedPaths, relative, isDirectory);
        if (relative.Count(character => character == SlashCharacter) + RelativeCountStep > executionOptions.Value.MaximumDepth)
        {
            throw NativeTextErrors.BoundExceeded();
        }
        if (isDirectory)
        {
            CaptureDirectory();
            return;
        }
        CaptureFile(path, relative);
    }

    private void CaptureDirectory()
    {
        if (++_directories > executionOptions.Value.MaximumDirectories)
        {
            throw NativeTextErrors.BoundExceeded();
        }
    }

    private void CaptureFile(string path, string relative)
    {
        if (_files.Count >= executionOptions.Value.MaximumFiles)
        {
            throw NativeTextErrors.BoundExceeded();
        }
        retained?.RequirePath(_generationPath);
        NativeTextFileIO.VerifyRegularFile(path);
        var share = retained is null ? FileShare.Read : FileShare.ReadWrite;
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, share,
            executionOptions.Value.HashBufferBytes, FileOptions.SequentialScan);
        var length = input.Length;
        var remaining = executionOptions.Value.MaximumDiskBytes - _bytes;
        var digest = NativeTextDigest.HashBounded(input, length, remaining, _budget, executionOptions: executionOptions);
        retained?.RequirePath(_generationPath);
        if (input.Length != length)
        {
            throw NativeTextErrors.Corrupt();
        }
        _bytes += length;
        _files.Add(new NativeTextFile(relative, length, digest));
    }
}
