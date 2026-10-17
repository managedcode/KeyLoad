using KeyLoad.Storage.ZoneTree.IO;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class RequestCqrsProbeRecordReader
{
    internal static byte[] ReadRecord(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(RequestCqrsProbeFixtureProtocol.PrivatePermissionsUnsupported);
        }
        var identity = OfflineRegularFile.Inspect(path);
        if (identity.Length is <= 0 or > RequestCqrsProbeFixtureProtocol.MaximumRecordBytes)
        {
            throw new IOException(RequestCqrsProbeFixtureProtocol.RecordLimitExceeded);
        }
        if (File.GetUnixFileMode(path) != RequestCqrsProbeFileValidation.PrivateFileMode)
        {
            throw new IOException(RequestCqrsProbeFixtureProtocol.InvalidControlEntry);
        }
        var bytes = new byte[checked((int)identity.Length)];
        using var input = OfflineRegularFile.OpenWithIdentity(path, identity, FileAccess.Read, FileShare.Read, 4096);
        if (input.Length != bytes.Length)
        {
            throw new IOException(RequestCqrsProbeFixtureProtocol.RecordLimitExceeded);
        }
        input.ReadExactly(bytes);
        if (input.ReadByte() != -1)
        {
            throw new IOException(RequestCqrsProbeFixtureProtocol.RecordLimitExceeded);
        }
        return bytes;
    }

}
