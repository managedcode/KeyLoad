using KeyLoad.Server;
using KeyLoad.Storage.IO;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Owns the fixture's private control paths and bounded no-overwrite files.</summary>
internal static class RequestCqrsProbeFileStore
{
    internal static void WriteAtomic(string directory, string fileName, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length is <= 0 or > RequestCqrsProbeFixtureProtocol.MaximumRecordBytes
            || !RequestCqrsProbeFileValidation.IsAllowedFileName(fileName)
            || fileName.StartsWith(RequestCqrsProbeFileValidation.TemporaryPrefix, StringComparison.Ordinal))
        { throw new IOException(RequestCqrsProbeFixtureProtocol.RecordLimitExceeded); }
        RequestCqrsProbeFileValidation.EnsureQuota(directory, bytes.Length);
        var destination = Path.Combine(directory, fileName);
        if (File.Exists(destination) || Directory.Exists(destination))
        { throw new IOException(RequestCqrsProbeFixtureProtocol.DuplicateControlFile); }

        var temporary = Path.Combine(directory, RequestCqrsProbeFileValidation.TemporaryPrefix
            + Guid.NewGuid().ToString("N") + ".tmp");
        var failures = new List<Exception>();
        var moved = false;
        ServerFailureObserver.Observe(() =>
        {
            WriteTemporary(temporary, bytes);
            File.Move(temporary, destination);
            moved = true;
        }, failures);
        CleanupTemporary(temporary, failures);
        if (moved && failures.Count > 0)
        { RequestCqrsProbeAtomicWriteSettlement.RemoveFailedPublication(directory, fileName, bytes, failures); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static void WriteTemporary(string path, ReadOnlySpan<byte> bytes)
    {
        if (OperatingSystem.IsWindows())
        { throw new PlatformNotSupportedException(RequestCqrsProbeFixtureProtocol.PrivatePermissionsUnsupported); }
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            BufferSize = 4096,
            Options = FileOptions.WriteThrough,
            UnixCreateMode = RequestCqrsProbeFileValidation.PrivateFileMode
        };
        using var output = new FileStream(path, options);
        output.Write(bytes);
        output.Flush(flushToDisk: true);
    }

    private static void CleanupTemporary(string path, List<Exception> failures)
        => ServerFailureObserver.Observe(() =>
        {
            if (File.Exists(path))
            { File.Delete(path); }
        }, failures);

    internal static void EnsureCanWrite(string directory, string fileName, int bytes)
    {
        if (bytes is <= 0 or > RequestCqrsProbeFixtureProtocol.MaximumRecordBytes
            || !RequestCqrsProbeFileValidation.IsAllowedFileName(fileName) || fileName.StartsWith(RequestCqrsProbeFileValidation.TemporaryPrefix, StringComparison.Ordinal))
        { throw new IOException(RequestCqrsProbeFixtureProtocol.InvalidControlEntry); }
        RequestCqrsProbeFileValidation.EnsureQuota(directory, bytes);
        var path = Path.Combine(directory, fileName);
        if (File.Exists(path) || Directory.Exists(path))
        { throw new IOException(RequestCqrsProbeFixtureProtocol.DuplicateControlFile); }
    }

    internal static void VerifyExactFile(string directory, string fileName, byte[] expected)
    {
        var actual = ReadRecord(Path.Combine(directory, fileName));
        if (!actual.AsSpan().SequenceEqual(expected))
        { throw new IOException(RequestCqrsProbeFixtureProtocol.OwnerChanged); }
    }

    internal static void DeleteExactFile(string directory, string fileName, byte[] expected)
    {
        VerifyExactFile(directory, fileName, expected);
        File.Delete(Path.Combine(directory, fileName));
    }

    internal static byte[] ReadRecord(string path)
    {
        if (OperatingSystem.IsWindows())
        { throw new PlatformNotSupportedException(RequestCqrsProbeFixtureProtocol.PrivatePermissionsUnsupported); }
        var identity = OfflineRegularFile.Inspect(path);
        if (identity.Length is <= 0 or > RequestCqrsProbeFixtureProtocol.MaximumRecordBytes)
        { throw new IOException(RequestCqrsProbeFixtureProtocol.RecordLimitExceeded); }
        if (File.GetUnixFileMode(path) != RequestCqrsProbeFileValidation.PrivateFileMode)
        { throw new IOException(RequestCqrsProbeFixtureProtocol.InvalidControlEntry); }
        var bytes = new byte[checked((int)identity.Length)];
        using var input = OfflineRegularFile.OpenWithIdentity(path, identity, FileAccess.Read, FileShare.Read, 4096);
        if (input.Length != bytes.Length)
        { throw new IOException(RequestCqrsProbeFixtureProtocol.RecordLimitExceeded); }
        input.ReadExactly(bytes);
        if (input.ReadByte() != -1)
        { throw new IOException(RequestCqrsProbeFixtureProtocol.RecordLimitExceeded); }
        return bytes;
    }

    internal static void VerifyOwnerFile(string directory, byte[] expected)
    {
        var actual = ReadRecord(Path.Combine(directory, RequestCqrsProbeFixtureProtocol.OwnerFileName));
        if (!actual.AsSpan().SequenceEqual(expected))
        { throw new IOException(RequestCqrsProbeFixtureProtocol.OwnerChanged); }
    }

    internal static void DeleteOwnedTree(string root, IReadOnlyDictionary<string, string> nodePaths,
        IReadOnlyDictionary<string, byte[]> ownerRecords, bool requireAllOwners)
    {
        RequestCqrsProbeFileValidation.ValidateDirectory(root);
        if (nodePaths.Count > RequestCqrsRf3Protocol.NodeCount
            || (requireAllOwners && nodePaths.Count != RequestCqrsRf3Protocol.NodeCount))
        { throw new IOException(RequestCqrsProbeFixtureProtocol.InvalidControlEntry); }
        var rootEntries = Directory.EnumerateFileSystemEntries(root).Take(RequestCqrsRf3Protocol.NodeCount + 1).ToArray();
        if (rootEntries.Length != nodePaths.Count
            || rootEntries.Any(path => !nodePaths.Values.Contains(path, StringComparer.Ordinal)))
        { throw new IOException(RequestCqrsProbeFixtureProtocol.InvalidControlEntry); }
        foreach (var pair in nodePaths)
        {
            var node = pair.Value;
            var expectedPath = Path.GetFullPath(Path.Combine(root, pair.Key));
            if (pair.Key is not (RequestCqrsRf3Protocol.Node1 or RequestCqrsRf3Protocol.Node2 or RequestCqrsRf3Protocol.Node3)
                || !string.Equals(Path.GetFullPath(node), expectedPath, StringComparison.Ordinal))
            { throw new IOException(RequestCqrsProbeFixtureProtocol.InvalidControlEntry); }
            RequestCqrsProbeFileValidation.ValidateDirectory(node);
            VerifyNodeOwner(node, pair.Key, ownerRecords, requireAllOwners);
            var files = RequestCqrsProbeFileValidation.ValidateContents(node);
            foreach (var file in files)
            { File.Delete(file); }
            Directory.Delete(node, recursive: false);
        }
        if (Directory.EnumerateFileSystemEntries(root).Take(1).Any())
        { throw new IOException(RequestCqrsProbeFixtureProtocol.InvalidControlEntry); }
        Directory.Delete(root, recursive: false);
    }

    private static void VerifyNodeOwner(string node, string nodeName,
        IReadOnlyDictionary<string, byte[]> ownerRecords, bool requireAllOwners)
    {
        if (ownerRecords.TryGetValue(nodeName, out var expectedOwner)
            && File.Exists(Path.Combine(node, RequestCqrsProbeFixtureProtocol.OwnerFileName)))
        {
            VerifyOwnerFile(node, expectedOwner);
            return;
        }
        if (requireAllOwners)
        { throw new IOException(RequestCqrsProbeFixtureProtocol.OwnerChanged); }
    }
}
