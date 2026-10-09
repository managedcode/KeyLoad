using System.Security.Cryptography;
using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

/// <summary>Continues only the proven original prefix in one admitted unpublished native slot.</summary>
internal static class ZoneTreeRestoreSlotPrefix
{
    private const int Start = 0;
    private const int NoBytes = 0;
    private const string Invalid = "The retained restore slot source journal prefix is inconsistent.";

    internal static void Continue(string backup, string stage, ZoneTreeBackupRestoreManifestFile expected,
        ZoneTreeStorageExecutionOptions policy, CancellationToken ct)
    {
        var sourcePath = Path.Combine(backup, JournalFileName);
        var targetPath = Path.Combine(stage, JournalFileName);
        RequireRegular(sourcePath);
        if (File.Exists(targetPath))
        { RequireRegular(targetPath); }
        using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            policy.StreamBufferBytes);
        if (source.Length != expected.Length || expected.Name != JournalFileName)
        { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
        using var target = new FileStream(targetPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
            policy.StreamBufferBytes);
        if (target.Length > expected.Length)
        { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
        RequirePrefix(source, target, policy.FileBufferBytes, null, ct);
        var buffer = new byte[policy.FileBufferBytes];
        while (target.Position < expected.Length)
        {
            ct.ThrowIfCancellationRequested();
            var length = (int)Math.Min(buffer.Length, expected.Length - target.Position);
            source.ReadExactly(buffer.AsSpan(Start, length));
            target.Write(buffer.AsSpan(Start, length));
        }
        target.Flush(true);
        target.Position = Start;
        if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(target)), expected.Checksum,
            StringComparison.Ordinal))
        { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
        ct.ThrowIfCancellationRequested();
    }

    internal static void RequireOriginalPrefix(string backup, string stage,
        ZoneTreeBackupRestoreManifestFile expected, ZoneTreeStorageExecutionOptions policy, CancellationToken ct)
    {
        var sourcePath = Path.Combine(backup, JournalFileName);
        var targetPath = Path.Combine(stage, JournalFileName);
        RequireRegular(sourcePath);
        RequireRegular(targetPath);
        using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, policy.StreamBufferBytes);
        using var target = new FileStream(targetPath, FileMode.Open, FileAccess.Read, FileShare.Read, policy.StreamBufferBytes);
        if (source.Length != expected.Length || target.Length < expected.Length)
        { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
        RequirePrefix(source, target, policy.FileBufferBytes, expected.Length, ct);
    }

    private static void RequirePrefix(FileStream source, FileStream target, int bufferBytes,
        long? fixedLength, CancellationToken ct)
    {
        var originalLength = fixedLength ?? target.Length;
        var sourceBytes = new byte[bufferBytes];
        var targetBytes = new byte[bufferBytes];
        var remaining = originalLength;
        while (remaining > NoBytes)
        {
            ct.ThrowIfCancellationRequested();
            var length = (int)Math.Min(sourceBytes.Length, remaining);
            source.ReadExactly(sourceBytes.AsSpan(Start, length));
            target.ReadExactly(targetBytes.AsSpan(Start, length));
            if (!sourceBytes.AsSpan(Start, length).SequenceEqual(targetBytes.AsSpan(Start, length)))
            { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
            remaining -= length;
        }
    }

    private static void RequireRegular(string path)
    {
        if ((File.GetAttributes(path) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != NoBytes)
        { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
    }
}
