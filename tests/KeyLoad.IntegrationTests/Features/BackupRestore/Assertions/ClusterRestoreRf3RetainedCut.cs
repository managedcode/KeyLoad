using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

/// <summary>Original stopped fixture bytes, not a source of database authority or synthesized receipts.</summary>
internal static class ClusterRestoreRf3RetainedCut
{
    internal static string OperationRoot(ClusterRestoreRf3Fixture target)
        => Path.Combine(Path.GetDirectoryName(target.DataRoot)
            ?? throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid),
            ClusterRestoreRf3ResumeProtocol.OperationPrefix + target.OperationId.ToString(ClusterRestoreRf3Protocol.IdentityFormat));

    internal static byte[] Plan(ClusterRestoreRf3Fixture target)
    {
        var path = Path.Combine(OperationRoot(target), ClusterRestoreRf3ResumeProtocol.PlanFile);
        var maximumBytes = IntegrationExecutionOptions.StorageExecution().Value.MaximumBackupManifestBytes;
        var file = new FileInfo(path);
        if (!file.Exists || file.Length > maximumBytes || (file.Attributes & FileAttributes.ReparsePoint) != default)
        { throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid); }
        using var original = file.Open(FileMode.Open, FileAccess.Read, FileShare.None);
        return SHA256.HashData(original);
    }

    internal static byte[] Target(ClusterRestoreRf3Fixture target) => Files(target.DataRoot);
    internal static byte[] Operation(ClusterRestoreRf3Fixture target) => Files(OperationRoot(target));

    private static byte[] Files(string root)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Visit(root, root, hash);
        return hash.GetHashAndReset();
    }

    private static void Visit(string root, string directory, IncrementalHash hash)
    {
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != default)
        { throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid); }
        Append(hash, Encoding.UTF8.GetBytes(Path.GetRelativePath(root, directory)));
        foreach (var path in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.Ordinal))
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != default)
            { throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid); }
            if ((attributes & FileAttributes.Directory) != default)
            { Visit(root, path, hash); }
            else
            {
                Append(hash, Encoding.UTF8.GetBytes(Path.GetRelativePath(root, path)));
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
                Append(hash, SHA256.HashData(stream));
            }
        }
    }

    private static void Append(IncrementalHash hash, byte[] value)
    {
        Span<byte> length = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(length, value.LongLength);
        hash.AppendData(length);
        hash.AppendData(value);
    }
}
