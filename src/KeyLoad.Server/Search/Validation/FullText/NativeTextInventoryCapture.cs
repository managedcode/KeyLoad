using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

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
