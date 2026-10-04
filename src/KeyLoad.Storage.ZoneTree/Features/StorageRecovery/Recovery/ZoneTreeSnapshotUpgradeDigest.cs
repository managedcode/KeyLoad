using System.Buffers.Binary;
using System.Security.Cryptography;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeSnapshotUpgradeDigest
{
    internal static void Append(IncrementalHash digest, StorageMutation mutation)
    {
        ArgumentNullException.ThrowIfNull(digest);
        if (mutation is null || mutation.Key.IsEmpty || mutation.Value is not { } value)
        {
            throw Errors.Fail(ErrorCode.Corruption, ZoneTreePersistenceFormat.CheckpointRecordsInvalid);
        }

        AppendBytes(digest, mutation.Key.Span);
        AppendBytes(digest, value.Span);
    }

    internal static bool Equal(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
        => left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);

    internal static byte[] Finish(IncrementalHash digest)
    {
        ArgumentNullException.ThrowIfNull(digest);
        return digest.GetHashAndReset();
    }

    private static void AppendBytes(IncrementalHash digest, ReadOnlySpan<byte> bytes)
    {
        Span<byte> length = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64BigEndian(length, (ulong)bytes.Length);
        digest.AppendData(length);
        digest.AppendData(bytes);
    }
}
