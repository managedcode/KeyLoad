using System.ComponentModel;

namespace KeyLoad.Storage.IO;

internal static class OfflineNativeErrors
{
    private const int MacSymlinkLoopErrno = 62;
    private const int LinuxSymlinkLoopErrno = 40;
    private const int LinuxUnsupportedSyscallErrno = 38;
    private const int MissingFileErrno = 2;
    private const string MissingFileMessage = "Offline input file was not found.";
    private const string FileSystemFailureMessage = "Offline filesystem operation failed.";
    private const string ChangedInputMessage = "Offline input changed during validation.";
    private const string InvalidPath = "Offline input is not a supported regular file.";
    private const string UnsupportedPlatform = "Offline regular-file operations are unavailable on this platform.";

    internal static KeyLoadException Unsupported() => Errors.Fail(ErrorCode.FormatUnsupported, UnsupportedPlatform);

    internal static Exception FromErrno(int error, bool isMac)
    {
        if (error == (isMac ? MacSymlinkLoopErrno : LinuxSymlinkLoopErrno))
        { return Errors.Fail(ErrorCode.FormatUnsupported, InvalidPath); }
        if (!isMac && error == LinuxUnsupportedSyscallErrno)
        { return Unsupported(); }
        if (error == MissingFileErrno)
        { return new FileNotFoundException(MissingFileMessage); }
        return new IOException(FileSystemFailureMessage, new Win32Exception(error));
    }

    internal static bool IsUnavailable(Exception error) => error is DllNotFoundException
        or EntryPointNotFoundException or BadImageFormatException;

    internal static KeyLoadException InvalidEntry() => Errors.Fail(ErrorCode.FormatUnsupported, InvalidPath);

    internal static KeyLoadException ChangedEntry() => Errors.Fail(ErrorCode.Corruption,
        ChangedInputMessage);
}
