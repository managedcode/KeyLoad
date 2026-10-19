using System.Runtime.InteropServices;

namespace KeyLoad.Storage.ZoneTree.IO;

internal static class OfflineRegularFileOpenFlags
{
    private const int LinuxReadOnly = 0;
    private const int LinuxReadWrite = 0x2;
    private const int LinuxX64NoFollow = 0x20000;
    private const int LinuxArm64NoFollow = 0x8000;
    private const int LinuxCloseOnExec = 0x80000;
    private const int MacReadOnly = 0;
    private const int MacReadWrite = 0x2;
    private const int MacNoFollow = 0x100;
    private const int MacCloseOnExec = 0x1000000;
    private const int LinuxNonBlocking = 0x800;
    private const int MacNonBlocking = 4;
    private const int LinuxSynchronous = 0x101000;
    private const int MacSynchronous = 0x80;

    internal static (int Flags, bool IsMac) Create(FileAccess access, bool writeThrough)
    {
        var isMac = OperatingSystem.IsMacOS();
        var flags = isMac ? MacNoFollow | MacCloseOnExec : LinuxNoFollow() | LinuxCloseOnExec;
        if (isMac)
        {
            flags |= access == FileAccess.Read ? MacReadOnly : MacReadWrite;
            flags |= MacNonBlocking;
        }
        else
        {
            flags |= access == FileAccess.Read ? LinuxReadOnly : LinuxReadWrite;
            flags |= LinuxNonBlocking;
        }
        if (writeThrough)
        {
            flags |= isMac ? MacSynchronous : LinuxSynchronous;
        }
        return (flags, isMac);
    }

    private static int LinuxNoFollow() => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => LinuxX64NoFollow,
        Architecture.Arm64 => LinuxArm64NoFollow,
        _ => throw OfflineErrors.Unsupported()
    };
}
