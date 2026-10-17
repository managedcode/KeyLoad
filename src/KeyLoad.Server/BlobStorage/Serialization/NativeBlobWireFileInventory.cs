using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.BlobStorage;

internal sealed class NativeBlobWireFileInventory(string root, IOptions<RequestProbeExecutionOptions> limits)
{
    private const int InitialFileCount = 0;
    private const long InitialAggregateBytes = 0;
    private const long EmptyLockBytes = 0;
    private const FileAttributes NoRejectedAttributes = 0;
    private const int FirstSlot = 0;
    private const int FirstResultOrdinal = 1;
    private readonly HashSet<string> names = Names(limits.Value.MaximumActiveGates);

    internal (int Count, long Bytes) Read()
    {
        RequestCqrsProbePaths.RequireDirectory(root, root);
        var count = InitialFileCount;
        var bytes = InitialAggregateBytes;
        foreach (var path in Directory.EnumerateFileSystemEntries(root))
        {
            if (++count > limits.Value.MaximumFiles)
            {
                throw Invalid();
            }
            var name = Path.GetFileName(path);
            if (!names.Contains(name) && !Temporary(name))
            {
                throw Invalid();
            }
            var length = RequireFile(path);
            if (length > limits.Value.MaximumRecordBytes)
            {
                throw Invalid();
            }
            bytes = checked(bytes + length);
            if (bytes > limits.Value.MaximumAggregateBytes)
            {
                throw Invalid();
            }
            if (name.EndsWith(NativeBlobWireProbeProtocol.LockSuffix, StringComparison.Ordinal) && length != EmptyLockBytes)
            {
                throw Invalid();
            }
        }
        return (count, bytes);
    }

    internal static long RequireFile(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || (info.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != NoRejectedAttributes)
        {
            throw Invalid();
        }
        RequestCqrsProbePaths.RequirePrivateMode(path, RequestCqrsProbeProtocol.PrivateFileMode);
        return info.Length;
    }

    private static HashSet<string> Names(int slots)
    {
        var result = new HashSet<string>(StringComparer.Ordinal)
        { NativeBlobWireProbeProtocol.OwnerFile, NativeBlobWireProbeProtocol.ArmsFile, NativeBlobWireProbeProtocol.PublicationFile };
        for (var slot = FirstSlot; slot < slots; slot++)
        {
            result.Add(NativeBlobWireNames.Slot(slot));
        }
        foreach (var identity in Enum.GetValues<NativeBlobWireIdentity>())
        {
            result.Add(NativeBlobWireNames.Claim(identity));
            result.Add(NativeBlobWireNames.Completion(identity));
            for (var ordinal = FirstResultOrdinal; ordinal <= NativeBlobWireProbeProtocol.MaximumFaultCount; ordinal++)
            {
                result.Add(NativeBlobWireNames.Result(identity, ordinal));
            }
        }
        return result;
    }

    private static bool Temporary(string name)
        => name.StartsWith(RequestCqrsProbeProtocol.TemporaryFilePrefix, StringComparison.Ordinal)
            && name.EndsWith(NativeBlobWireNames.TemporarySuffix, StringComparison.Ordinal)
            && Guid.TryParseExact(name.AsSpan(RequestCqrsProbeProtocol.TemporaryFilePrefix.Length,
                name.Length - RequestCqrsProbeProtocol.TemporaryFilePrefix.Length - NativeBlobWireNames.TemporarySuffix.Length), RequestCqrsProbeProtocol.SessionIdFormat, out _);
    private static InvalidOperationException Invalid() => new(NativeBlobWireProbeProtocol.InvalidEvidence);
}
