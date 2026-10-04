using System.ComponentModel;

namespace KeyLoad.Storage.IO;

internal static class OfflineNativeErrors
{
    private const string InvalidPath = "Offline input is not a supported regular file.";
    private const string UnsupportedPlatform = "Offline regular-file operations are unavailable on this platform.";

    internal static KeyLoadException Unsupported() => Errors.Fail(ErrorCode.FormatUnsupported, UnsupportedPlatform);

    internal static Exception FromErrno(int error, bool isMac)
    {
        if (error == (isMac ? 62 : 40))
        { return Errors.Fail(ErrorCode.FormatUnsupported, InvalidPath); }
        if (!isMac && error == 38)
        { return Unsupported(); }
        if (error == 2)
        { return new FileNotFoundException("Offline input file was not found."); }
        return new IOException("Offline filesystem operation failed.", new Win32Exception(error));
    }

    internal static bool IsUnavailable(Exception error) => error is DllNotFoundException
        or EntryPointNotFoundException or BadImageFormatException;

    internal static KeyLoadException InvalidEntry() => Errors.Fail(ErrorCode.FormatUnsupported, InvalidPath);

    internal static KeyLoadException ChangedEntry() => Errors.Fail(ErrorCode.Corruption,
        "Offline input changed during validation.");
}
