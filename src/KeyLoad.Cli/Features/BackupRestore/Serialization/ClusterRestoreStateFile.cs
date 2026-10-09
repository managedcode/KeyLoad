using System.Buffers.Binary;
using System.Security.Cryptography;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.Cli.Features.BackupRestore;

/// <summary>Owns bounded current native operation files with exact original payload checksums.</summary>
internal static class ClusterRestoreStateFile
{
    private const ulong Magic = 0x315253434C4BUL;
    private const int HeaderBytes = sizeof(ulong);
    private const int NoBytes = 0;
    private const string PendingSuffix = ".pending";
    private const string Invalid = "The native cluster restore operation state is corrupt or foreign.";

    internal sealed record ReadResult<T>(T Value, string Digest);

    internal static ReadResult<T> Read<T>(string path, ZoneTreeStorageExecutionOptions policy)
    {
        RequireRegular(path);
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, policy.StreamBufferBytes);
        if (file.Length <= HeaderBytes || file.Length > policy.MaximumBackupManifestBytes)
        { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
        var bytes = new byte[checked((int)file.Length)];
        file.ReadExactly(bytes);
        if (BinaryPrimitives.ReadUInt64LittleEndian(bytes) != Magic)
        { throw Errors.Fail(ErrorCode.FormatUnsupported, Invalid); }
        var envelope = NativeSerialization.Deserialize<ClusterRestoreStateEnvelope>(bytes.AsSpan(HeaderBytes));
        if (envelope is null || envelope.Version != ClusterRestoreStateEnvelope.CurrentVersion
            || envelope.Payload.IsEmpty || !CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(envelope.Payload.Span), envelope.Checksum.Span))
        { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
        var value = NativeSerialization.Deserialize<T>(envelope.Payload.Span) ?? throw Errors.Fail(ErrorCode.Corruption, Invalid);
        return new(value, Convert.ToHexStringLower(envelope.Checksum.Span));
    }

    internal static string Write<T>(string path, T value, ZoneTreeStorageExecutionOptions policy, bool replace)
    {
        var payload = NativeSerialization.Serialize(value);
        var checksum = SHA256.HashData(payload);
        var envelope = NativeSerialization.Serialize(new ClusterRestoreStateEnvelope(
            ClusterRestoreStateEnvelope.CurrentVersion, payload, checksum));
        var size = checked(HeaderBytes + envelope.Length);
        if (size > policy.MaximumBackupManifestBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, Invalid); }
        var temporary = path + PendingSuffix;
        if (File.Exists(temporary))
        { throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid); }
        if (File.Exists(path))
        { RequireRegular(path); }
        using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            policy.StreamBufferBytes, FileOptions.WriteThrough))
        {
            Span<byte> header = stackalloc byte[HeaderBytes];
            BinaryPrimitives.WriteUInt64LittleEndian(header, Magic);
            file.Write(header);
            file.Write(envelope);
            file.Flush(true);
        }
        _ = Read<T>(temporary, policy);
        File.Move(temporary, path, replace);
        var original = Read<T>(path, policy);
        if (!string.Equals(original.Digest, Convert.ToHexStringLower(checksum), StringComparison.Ordinal))
        { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
        return original.Digest;
    }

    internal static ReadResult<T> RecoverPending<T>(string path, ZoneTreeStorageExecutionOptions policy,
        Action<T> verifyActualNativeState)
    {
        var temporary = path + PendingSuffix;
        var original = Read<T>(temporary, policy);
        // Owning plan/progress validation re-reads REAL original slots before this checked rename.
        verifyActualNativeState(original.Value);
        if (File.Exists(path))
        { RequireRegular(path); }
        File.Move(temporary, path, overwrite: true);
        var published = Read<T>(path, policy);
        if (!string.Equals(published.Digest, original.Digest, StringComparison.Ordinal))
        { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
        return published;
    }

    private static void RequireRegular(string path)
    {
        if ((File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != NoBytes)
        { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
    }
}
