
namespace KeyLoad.UnitTests.CodeQuality;

internal static class CoverageDeploymentClosure
{
    private const int EndOfFile = -1;
    private const string SourceShrankDuringCopyMessage =
        "A source execution coverage file shrank during fixture creation.";
    private const string SourceGrewDuringCopyMessage =
        "A source execution coverage file grew during fixture creation.";

    internal static string CopyToRoot(string source, string destinationRoot, string name,
        CoverageExecutionOptions options)
    {
        var root = Path.GetFullPath(destinationRoot);
        var destination = Path.GetFullPath(Path.Combine(root, name));
        var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var rootInfo = new DirectoryInfo(root);
        if (!rootInfo.Exists || IsReparsePoint(rootInfo) || root.Length > options.MaximumPathCharacters
            || destination.Length > options.MaximumPathCharacters
            || !destination.StartsWith(rootPrefix, StringComparison.Ordinal)
            || IsOccupiedOrLinked(destination))
        {
            throw new InvalidDataException("A execution coverage deployment copy path is unsafe or already occupied.");
        }
        Copy(source, destination, options.MaximumFiles, options.MaximumFileBytes,
            options.MaximumTotalBytes, options.MaximumPathCharacters, options.ReadBufferBytes);
        return destination;
    }

    internal static void Copy(string source, string destination, int maximumFiles,
        int maximumFileBytes, long maximumTotalBytes, int maximumPathCharacters, int readBufferBytes)
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Execution coverage deployment permissions require a Unix filesystem.");
        }
        if (!Directory.Exists(source) || IsReparsePoint(new DirectoryInfo(source)))
        {
            throw new InvalidDataException("The source execution coverage deployment closure is missing or linked.");
        }
        Directory.CreateDirectory(destination);
        long totalBytes = 0;
        foreach (var file in EnumerateFiles(source, maximumFiles, maximumPathCharacters))
        {
            var sourceInfo = new FileInfo(file);
            if (!sourceInfo.Exists || sourceInfo.Length < 0 || sourceInfo.Length > maximumFileBytes)
            {
                throw new InvalidDataException("A source execution coverage file exceeds its admitted inventory or file bound.");
            }
            totalBytes = checked(totalBytes + sourceInfo.Length);
            if (totalBytes > maximumTotalBytes)
            {
                throw new InvalidDataException("The source execution coverage closure exceeds its configured byte bound.");
            }
            CopyFile(source, destination, file, sourceInfo.Length, maximumPathCharacters, readBufferBytes);
        }
    }

    internal static IEnumerable<string> EnumerateFiles(string root, int maximumFiles,
        int maximumPathCharacters)
    {
        var fullRoot = Path.GetFullPath(root);
        if (fullRoot.Length > maximumPathCharacters || !Directory.Exists(fullRoot)
                                                    || IsReparsePoint(new DirectoryInfo(fullRoot)))
        {
            throw new InvalidDataException("A execution coverage closure root is missing, linked or too long.");
        }
        var pending = new Stack<string>();
        pending.Push(fullRoot);
        var count = 0;
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                var information = (attributes & FileAttributes.Directory) != 0
                    ? (FileSystemInfo)new DirectoryInfo(entry) : new FileInfo(entry);
                if (entry.Length > maximumPathCharacters || IsReparsePoint(information))
                {
                    throw new InvalidDataException("A execution coverage closure contains an unsafe or overlong path.");
                }
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(entry);
                    continue;
                }
                if ((attributes & FileAttributes.Device) != 0 || ++count > maximumFiles)
                {
                    throw new InvalidDataException("A execution coverage closure contains an unsafe or excessive file inventory.");
                }
                yield return entry;
            }
        }
    }

    private static void CopyFile(string source, string destination, string file,
        long expectedLength, int maximumPathCharacters, int readBufferBytes)
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Execution coverage deployment permissions require a Unix filesystem.");
        }
        var relative = Path.GetRelativePath(source, file);
        var target = Path.Combine(destination, relative);
        var destinationRoot = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar)
                              + Path.DirectorySeparatorChar;
        if (target.Length > maximumPathCharacters || !Path.GetFullPath(target).StartsWith(
                destinationRoot, StringComparison.Ordinal))
        {
            throw new InvalidDataException("A execution coverage copy path escaped its bounded destination.");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        CopyExactFile(file, target, expectedLength, readBufferBytes);
        var copied = new FileInfo(target);
        if (!copied.Exists || copied.Length != expectedLength || IsReparsePoint(copied))
        {
            throw new InvalidDataException("A copied execution coverage file changed during fixture creation.");
        }
        File.SetUnixFileMode(target, File.GetUnixFileMode(file));
    }

    private static void CopyExactFile(string source, string destination, long expectedLength,
        int readBufferBytes)
    {
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read,
            readBufferBytes, FileOptions.SequentialScan);
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            readBufferBytes, FileOptions.SequentialScan);
        var buffer = new byte[readBufferBytes];
        var remaining = expectedLength;
        while (remaining > 0)
        {
            var requested = (int)Math.Min(buffer.Length, remaining);
            var read = input.Read(buffer, 0, requested);
            if (read == 0)
            {
                throw new InvalidDataException(SourceShrankDuringCopyMessage);
            }
            output.Write(buffer, 0, read);
            remaining -= read;
        }
        if (input.ReadByte() != EndOfFile)
        {
            throw new InvalidDataException(SourceGrewDuringCopyMessage);
        }
    }

    private static bool IsReparsePoint(FileSystemInfo entry)
        => entry.LinkTarget is not null || (entry.Attributes & FileAttributes.ReparsePoint) != 0;

    private static bool IsOccupiedOrLinked(string path)
        => Directory.Exists(path) || File.Exists(path)
                                  || new DirectoryInfo(path).LinkTarget is not null || new FileInfo(path).LinkTarget is not null;
}
