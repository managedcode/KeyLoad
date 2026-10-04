using KeyLoad.Core;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextFileIO
{
    private const UnixFileMode PrivateDirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode PrivateFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    internal static T ReadEnvelope<T>(string path, long maximumBytes)
    {
        VerifyRegularFile(path);
        var info = new FileInfo(path);
        if (info.Length is <= 0 || info.Length > maximumBytes)
        {
            throw NativeTextErrors.BoundExceeded();
        }
        return NativeTextEnvelopeCodec.Decode<T>(File.ReadAllBytes(path));
    }

    internal static void WriteEnvelope<T>(string path, T value, long maximumBytes)
    {
        var bytes = NativeTextEnvelopeCodec.Encode(value);
        WriteEncodedEnvelope(path, bytes, maximumBytes);
    }

    internal static void WriteEncodedEnvelope(string path, byte[] bytes, long maximumBytes)
    {
        if (bytes.LongLength is <= 0 || bytes.LongLength > maximumBytes)
        {
            throw NativeTextErrors.BoundExceeded();
        }
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            4_096, FileOptions.WriteThrough);
        output.Write(bytes);
        output.Flush(true);
        SetPrivateFileMode(path);
    }

    internal static (int Files, long Bytes) MeasureRegularFiles(string directory, int maximumFiles,
        long maximumBytes, ReadExecutionBudget? budget = null)
    {
        VerifyDirectory(directory);
        var files = 0;
        var directories = 0;
        long bytes = 0;
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.AllDirectories))
        {
            budget?.Check();
            var relative = Path.GetRelativePath(directory, entry);
            if (relative.Count(character => character == Path.DirectorySeparatorChar) + 1
                > NativeTextProtocol.MaximumDepth)
            {
                throw NativeTextErrors.BoundExceeded();
            }
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw NativeTextErrors.Ownership();
            }
            if ((attributes & FileAttributes.Directory) == 0)
            {
                if (++files > maximumFiles)
                {
                    throw NativeTextErrors.BoundExceeded();
                }
                var length = new FileInfo(entry).Length;
                if (length > maximumBytes - bytes)
                {
                    throw NativeTextErrors.BoundExceeded();
                }
                bytes += length;
            }
            else if (++directories > NativeTextProtocol.MaximumDirectories)
            {
                throw NativeTextErrors.BoundExceeded();
            }
        }
        return (files, bytes);
    }

    internal static void VerifyBoundedFile(string path)
    {
        VerifyRegularFile(path);
        if (new FileInfo(path).Length > NativeTextProtocol.MaximumDiskBytes)
        {
            throw NativeTextErrors.BoundExceeded();
        }
    }

    internal static void VerifyDirectory(string path)
    {
        if (!Directory.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw NativeTextErrors.Ownership();
        }
    }

    internal static void VerifyRegularFile(string path)
    {
        if (!File.Exists(path) || (File.GetAttributes(path)
            & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
        {
            throw NativeTextErrors.Ownership();
        }
    }

    internal static void SetPrivateDirectoryMode(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, PrivateDirectoryMode);
        }
    }

    internal static void SetPrivateFileMode(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, PrivateFileMode);
        }
    }
}
