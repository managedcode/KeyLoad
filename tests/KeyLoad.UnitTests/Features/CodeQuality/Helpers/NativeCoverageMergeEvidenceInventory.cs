using KeyLoad.AppHost.Features.CodeQuality;

namespace KeyLoad.UnitTests.Features.CodeQuality;

internal static class NativeCoverageMergeEvidenceInventory
{
    internal sealed record FileEntry(string SourcePath, string RelativePath, long Length, DateTime LastWriteTimeUtc);

    internal static IReadOnlyList<FileEntry> Read(string sourceRoot, NativeCoverageExecutionOptions options)
    {
        var root = Path.GetFullPath(sourceRoot);
        var rootInfo = new DirectoryInfo(root);
        if (!rootInfo.Exists || IsReparsePoint(rootInfo))
        {
            throw new InvalidDataException("The native coverage evidence root is missing or linked.");
        }
        var pending = new Stack<DirectoryInfo>();
        var files = new List<FileEntry>();
        pending.Push(rootInfo);
        var entries = 0;
        long totalBytes = 0;
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            ReadDirectory(directory, root, options, pending, files, ref entries, ref totalBytes);
        }
        return files;
    }

    private static void ReadDirectory(DirectoryInfo directory, string root, NativeCoverageExecutionOptions options,
        Stack<DirectoryInfo> pending, List<FileEntry> files, ref int entries, ref long totalBytes)
    {
        if (IsReparsePoint(directory))
        {
            throw new InvalidDataException("A native coverage evidence directory is linked.");
        }
        foreach (var path in Directory.EnumerateFileSystemEntries(directory.FullName))
        {
            if (entries >= options.MaximumFiles)
            {
                throw new InvalidDataException("The native coverage evidence entry count exceeds its bound.");
            }
            entries++;
            AddEntry(path, root, options, pending, files, ref totalBytes);
        }
    }

    private static void AddEntry(string path, string root, NativeCoverageExecutionOptions options,
        Stack<DirectoryInfo> pending, List<FileEntry> files, ref long totalBytes)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("A native coverage evidence entry is linked.");
        }
        if ((attributes & FileAttributes.Directory) != 0)
        {
            pending.Push(new DirectoryInfo(path));
            return;
        }
        var relative = Path.GetRelativePath(root, path);
        var info = new FileInfo(path);
        if (!info.Exists || info.Length < 0 || info.Length > options.MaximumFileBytes
            || path.Length > options.MaximumPathCharacters || relative.Length > options.MaximumPathCharacters
            || totalBytes > options.MaximumTotalBytes - info.Length)
        {
            throw new InvalidDataException("A native coverage evidence file exceeds its bound.");
        }
        totalBytes += info.Length;
        files.Add(new(path, relative, info.Length, info.LastWriteTimeUtc));
    }

    private static bool IsReparsePoint(FileSystemInfo info) => info.LinkTarget is not null
        || (info.Attributes & FileAttributes.ReparsePoint) != 0;
}
