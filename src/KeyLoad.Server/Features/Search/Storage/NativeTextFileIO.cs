using Microsoft.Extensions.Options;
using KeyLoad.Core;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextFileIO
{
    private const UnixFileMode PrivateDirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode PrivateFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    internal static T ReadEnvelope<T>(string path, long maximumBytes)
    {
        const int InfoLengthEmptyCount = 0;

        VerifyRegularFile(path);
        var info = new FileInfo(path);
        if (info.Length is <= InfoLengthEmptyCount || info.Length > maximumBytes)
        {
            throw NativeTextErrors.BoundExceeded();
        }
        return NativeTextEnvelopeCodec.Decode<T>(File.ReadAllBytes(path));
    }

    internal static void WriteEnvelope<T>(string path, T value, long maximumBytes, IOptions<NativeTextExecutionOptions> executionOptions)
    {
        var bytes = NativeTextEnvelopeCodec.Encode(value);
        WriteEncodedEnvelope(path, bytes, maximumBytes, executionOptions: executionOptions);
    }

    internal static void WriteEncodedEnvelope(string path, byte[] bytes, long maximumBytes, IOptions<NativeTextExecutionOptions> executionOptions)
    {
        const int LongLengthEmptyCount = 0;

        if (bytes.LongLength is <= LongLengthEmptyCount || bytes.LongLength > maximumBytes)
        {
            throw NativeTextErrors.BoundExceeded();
        }
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            executionOptions.Value.FileBufferBytes, FileOptions.WriteThrough);
        output.Write(bytes);
        output.Flush(true);
        SetPrivateFileMode(path);
    }

    internal static (int Files, long Bytes) MeasureRegularFiles(string directory, int maximumFiles, long maximumBytes, IOptions<NativeTextExecutionOptions> executionOptions, ReadExecutionBudget? budget = null)
    {
        const int FilesInitialValue = 0;
        const int DirectoriesInitialValue = 0;
        const int BytesInitialValue = 0;
        const string AllEntriesSearchPattern = "*";
        const int RelativeCountStep = 1;
        const int EmptyAttributesFileAttributesReparsePoint = 0;
        const int EmptyAttributesFileAttributesDirectory = 0;

        VerifyDirectory(directory);
        var files = FilesInitialValue;
        var directories = DirectoriesInitialValue;
        long bytes = BytesInitialValue;
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory, AllEntriesSearchPattern, SearchOption.AllDirectories))
        {
            budget?.Check();
            var relative = Path.GetRelativePath(directory, entry);
            if (relative.Count(character => character == Path.DirectorySeparatorChar) + RelativeCountStep
                > executionOptions.Value.MaximumDepth)
            {
                throw NativeTextErrors.BoundExceeded();
            }
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != EmptyAttributesFileAttributesReparsePoint)
            {
                throw NativeTextErrors.Ownership();
            }
            if ((attributes & FileAttributes.Directory) == EmptyAttributesFileAttributesDirectory)
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
            else if (++directories > executionOptions.Value.MaximumDirectories)
            {
                throw NativeTextErrors.BoundExceeded();
            }
        }
        return (files, bytes);
    }

    internal static void VerifyBoundedFile(string path, IOptions<NativeTextExecutionOptions> executionOptions)
    {
        VerifyRegularFile(path);
        if (new FileInfo(path).Length > executionOptions.Value.MaximumDiskBytes)
        {
            throw NativeTextErrors.BoundExceeded();
        }
    }

    internal static void VerifyDirectory(string path)
    {
        const int EmptyFileGetAttributesPathFileAttributesReparsePoint = 0;

        if (!Directory.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != EmptyFileGetAttributesPathFileAttributesReparsePoint)
        {
            throw NativeTextErrors.Ownership();
        }
    }

    internal static void VerifyRegularFile(string path)
    {
        const int NoDisallowedFileAttributes = 0;

        if (!File.Exists(path) || (File.GetAttributes(path)
            & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != NoDisallowedFileAttributes)
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
