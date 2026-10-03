using KeyLoad.Core;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextOwnedInventory
{
    internal static void ValidateTrackedLayout(string generationPath, NativeTextOwnedPath[] ownedPaths,
        ReadExecutionBudget? budget, bool allowMissingNative)
    {
        NativeTextValidation.ValidateOwnedPaths(ownedPaths);
        var nativePath = Path.Combine(generationPath, NativeTextProtocol.NativeDirectory);
        if (!Directory.Exists(nativePath))
        {
            VerifyMissingNative(nativePath, allowMissingNative);
            return;
        }
        NativeTextFileIO.VerifyDirectory(nativePath);
        var counts = new LayoutCounts { Entries = 1, Directories = 1 };
        foreach (var path in Directory.EnumerateFileSystemEntries(nativePath, "*", SearchOption.AllDirectories))
        {
            budget?.Check();
            var attributes = File.GetAttributes(path);
            VerifyEntry(generationPath, path, attributes, ownedPaths, counts);
        }
    }

    internal static NativeTextFile[] CaptureFiles(string generationPath, NativeTextOwnedPath[] ownedPaths,
        ReadExecutionBudget? budget)
        => new NativeTextInventoryCapture(generationPath, ownedPaths, budget).Capture();

    internal static void RequireOwnedPath(NativeTextOwnedPath[] ownedPaths, string relative, bool directory)
    {
        if (!ownedPaths.Any(owned => owned.RelativePath == relative && owned.IsDirectory == directory))
        {
            throw NativeTextErrors.Ownership();
        }
    }

    private static void VerifyMissingNative(string path, bool allowMissing)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0 || (attributes & FileAttributes.Directory) == 0)
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

    private static void VerifyEntry(string generationPath, string path, FileAttributes attributes,
        NativeTextOwnedPath[] ownedPaths, LayoutCounts counts)
    {
        if (++counts.Entries > NativeTextProtocol.MaximumEntries)
        {
            throw NativeTextErrors.BoundExceeded();
        }
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw NativeTextErrors.Ownership();
        }
        var relative = NativeTextPath.RelativePath(generationPath, path);
        var directory = (attributes & FileAttributes.Directory) != 0;
        RequireOwnedPath(ownedPaths, relative, directory);
        if (relative.Count(character => character == '/') + 1 > NativeTextProtocol.MaximumDepth)
        {
            throw NativeTextErrors.BoundExceeded();
        }
        if (directory)
        {
            if (++counts.Directories > NativeTextProtocol.MaximumDirectories)
            {
                throw NativeTextErrors.BoundExceeded();
            }
            return;
        }
        var length = new FileInfo(path).Length;
        if (++counts.Files > NativeTextProtocol.MaximumFiles
            || length > NativeTextProtocol.MaximumDiskBytes - counts.Bytes)
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
    private readonly string _generationPath;
    private readonly NativeTextOwnedPath[] _ownedPaths;
    private readonly ReadExecutionBudget? _budget;
    private readonly List<NativeTextFile> _files = [];
    private int _entries = 1;
    private int _directories = 1;
    private long _bytes;

    internal NativeTextInventoryCapture(string generationPath, NativeTextOwnedPath[] ownedPaths,
        ReadExecutionBudget? budget)
    {
        _generationPath = generationPath;
        _ownedPaths = ownedPaths;
        _budget = budget;
    }

    internal NativeTextFile[] Capture()
    {
        var nativePath = Path.Combine(_generationPath, NativeTextProtocol.NativeDirectory);
        foreach (var path in Directory.EnumerateFileSystemEntries(nativePath, "*", SearchOption.AllDirectories))
        {
            _budget?.Check();
            CaptureEntry(path, File.GetAttributes(path));
        }
        _files.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.RelativePath, right.RelativePath));
        return _files.ToArray();
    }

    private void CaptureEntry(string path, FileAttributes attributes)
    {
        if (++_entries > NativeTextProtocol.MaximumEntries)
        {
            throw NativeTextErrors.BoundExceeded();
        }
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw NativeTextErrors.Ownership();
        }
        var relative = NativeTextPath.RelativePath(_generationPath, path);
        var isDirectory = (attributes & FileAttributes.Directory) != 0;
        NativeTextOwnedInventory.RequireOwnedPath(_ownedPaths, relative, isDirectory);
        if (relative.Count(character => character == '/') + 1 > NativeTextProtocol.MaximumDepth)
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
        if (++_directories > NativeTextProtocol.MaximumDirectories)
        {
            throw NativeTextErrors.BoundExceeded();
        }
    }

    private void CaptureFile(string path, string relative)
    {
        if (_files.Count >= NativeTextProtocol.MaximumFiles)
        {
            throw NativeTextErrors.BoundExceeded();
        }
        NativeTextFileIO.VerifyRegularFile(path);
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            NativeTextProtocol.HashBufferBytes, FileOptions.SequentialScan);
        var length = input.Length;
        var remaining = NativeTextProtocol.MaximumDiskBytes - _bytes;
        var digest = NativeTextDigest.HashBounded(input, length, remaining, _budget);
        if (input.Length != length)
        {
            throw NativeTextErrors.Corrupt();
        }
        _bytes += length;
        _files.Add(new NativeTextFile(relative, length, digest));
    }
}
