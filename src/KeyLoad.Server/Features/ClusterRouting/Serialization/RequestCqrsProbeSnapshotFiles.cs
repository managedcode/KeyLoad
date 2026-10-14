using System.Security.Cryptography;
using KeyLoad.Storage.IO;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class RequestCqrsProbeSnapshotFiles
{
    internal const long EmptyByteCount = 0;
    private const int OverflowFileCount = 1;

    internal static string[] RequireInventory(string root, byte[] ownerBytes,
        IOptions<RequestProbeExecutionOptions> executionOptions)
    {
        RequestCqrsProbePaths.RequireDirectory(root);
        var currentOwner = RequestCqrsProbeFiles.ReadRecord(
            Path.Combine(root, RequestCqrsProbeProtocol.OwnerFile), executionOptions);
        if (!CryptographicOperations.FixedTimeEquals(ownerBytes, currentOwner))
        {
            throw Invalid();
        }
        var entries = Directory.EnumerateFileSystemEntries(root)
            .Take(executionOptions.Value.MaximumFiles + OverflowFileCount).ToArray();
        if (entries.Length > executionOptions.Value.MaximumFiles)
        {
            throw Invalid();
        }
        return entries;
    }

    internal static long ChargeEntry(string path, string name, long aggregateBytes,
        IOptions<RequestProbeExecutionOptions> executionOptions)
    {
        var observed = checked(aggregateBytes + ValidateEntry(path, name, executionOptions));
        if (observed > executionOptions.Value.MaximumAggregateBytes)
        {
            throw Invalid();
        }
        return observed;
    }

    internal static long ValidateEntry(string path, string name,
        IOptions<RequestProbeExecutionOptions> executionOptions)
    {
        var identity = OfflineRegularFile.Inspect(path);
        RequestCqrsProbePaths.RequirePrivateMode(path, RequestCqrsProbeProtocol.PrivateFileMode);
        var length = identity.Length;
        if (length > executionOptions.Value.MaximumRecordBytes || !RequestCqrsProbeKnownNames.KnownName(name))
        {
            throw Invalid();
        }
        return length;
    }

    private static InvalidOperationException Invalid() => new(RequestCqrsProbeProtocol.InvalidFiles);
}
