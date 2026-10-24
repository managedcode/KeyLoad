using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Search;

internal sealed class TextInventoryCapture
{
    private const int EntriesInitialValue = 1;
    private const int DirectoriesInitialValue = 1;

    private readonly IOptions<TextExecutionOptions> executionOptions;

    private readonly string _generationPath;
    private readonly TextOwnedPath[] _ownedPaths;
    private readonly ReadExecutionBudget? _budget;
    private readonly TextOnlineGenerationPin? retained;
    private readonly List<TextFile> _files = [];
    private int _entries = EntriesInitialValue;
    private int _directories = DirectoriesInitialValue;
    private long _bytes;

    internal TextInventoryCapture(string generationPath, TextOwnedPath[] ownedPaths, ReadExecutionBudget? budget, IOptions<TextExecutionOptions> executionOptions, TextOnlineGenerationPin? retained = null)
    {
        this.executionOptions = executionOptions;
        _generationPath = generationPath;
        _ownedPaths = ownedPaths;
        _budget = budget;
        this.retained = retained;
    }

    internal TextFile[] Capture()
    {
        const string AllEntriesSearchPattern = "*";

        retained?.RequirePath(_generationPath);
        var runtimePath = Path.Combine(_generationPath, TextProtocol.RuntimeDirectoryCore);
        foreach (var path in Directory.EnumerateFileSystemEntries(runtimePath, AllEntriesSearchPattern, SearchOption.AllDirectories))
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
            throw TextErrors.BoundExceeded();
        }
        if ((attributes & FileAttributes.ReparsePoint) != EmptyAttributesFileAttributesReparsePoint)
        {
            throw TextErrors.Ownership();
        }
        var relative = RuntimeTextPath.RelativePath(_generationPath, path);
        var isDirectory = (attributes & FileAttributes.Directory) != EmptyAttributesFileAttributesDirectory;
        TextOwnedInventory.RequireOwnedPath(_ownedPaths, relative, isDirectory);
        if (relative.Count(character => character == SlashCharacter) + RelativeCountStep > executionOptions.Value.MaximumDepth)
        {
            throw TextErrors.BoundExceeded();
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
            throw TextErrors.BoundExceeded();
        }
    }

    private void CaptureFile(string path, string relative)
    {
        if (_files.Count >= executionOptions.Value.MaximumFiles)
        {
            throw TextErrors.BoundExceeded();
        }
        retained?.RequirePath(_generationPath);
        TextFileIO.VerifyRegularFile(path);
        var share = retained is null ? FileShare.Read : FileShare.ReadWrite;
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, share,
            executionOptions.Value.HashBufferBytes, FileOptions.SequentialScan);
        var length = input.Length;
        var remaining = executionOptions.Value.MaximumDiskBytes - _bytes;
        var digest = TextDigest.HashBounded(input, length, remaining, _budget, executionOptions: executionOptions);
        retained?.RequirePath(_generationPath);
        if (input.Length != length)
        {
            throw TextErrors.Corrupt();
        }
        _bytes += length;
        _files.Add(new TextFile(relative, length, digest));
    }
}
