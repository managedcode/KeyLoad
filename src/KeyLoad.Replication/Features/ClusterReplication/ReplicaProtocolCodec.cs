using System.Buffers.Binary;
using System.Collections.Immutable;

namespace KeyLoad.Replication;

/// <summary>Encodes attributed native Orleans replica values behind a fixed version fence.</summary>
public static class ReplicaProtocolCodec
{
    /// <summary>Serializes a typed replica value without changing its exact operation content.</summary>
    /// <typeparam name="T">Replica value type.</typeparam>
    /// <param name="value">Attributed contract or supported native scalar.</param>
    /// <returns>Owned version-prefixed native bytes.</returns>
    public static byte[] Serialize<T>(T value)
    {
        var payload = NativeSerialization.Serialize(value);
        var bytes = new byte[checked(ReplicaProtocol.PayloadPrefixBytes + payload.Length)];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, ReplicaProtocol.PayloadMagic);
        payload.CopyTo(bytes, ReplicaProtocol.PayloadPrefixBytes);
        return bytes;
    }

    /// <summary>Reads a complete current-format native replica value.</summary>
    /// <typeparam name="T">Exact expected contract or scalar type.</typeparam>
    /// <param name="bytes">Complete version-prefixed native message.</param>
    /// <returns>The decoded value with original operation content.</returns>
    public static T Deserialize<T>(ReadOnlySpan<byte> bytes)
    {
        var payload = PayloadBytes(bytes);
        try
        {
            return NativeSerialization.Deserialize<T>(payload);
        }
        catch (KeyLoadException error) when (error.Code == ErrorCode.Corruption)
        {
            throw Errors.Fail(ErrorCode.Corruption, ReplicaPersistence.InvalidEncoding);
        }
    }

    internal static T DeserializeStored<T>(ReadOnlyMemory<byte> bytes, int maximumEntries)
    {
        _ = ReplicaNativeInspection.Inspect<T>(bytes, maximumEntries);
        return Deserialize<T>(bytes.Span);
    }

    /// <summary>Measures exact native bytes including the fixed replica version prefix.</summary>
    /// <typeparam name="T">Exact encoded value type.</typeparam>
    /// <param name="value">Attributed contract or native scalar to measure.</param>
    /// <returns>The complete encoded length.</returns>
    public static long Measure<T>(T value) => checked(ReplicaProtocol.PayloadPrefixBytes + NativeSerialization.Measure(value));

    /// <summary>Encodes the exact standalone batch contract used by append admission.</summary>
    /// <param name="entries">Initialized ordered entry collection.</param>
    /// <returns>Owned version-prefixed native batch bytes.</returns>
    public static byte[] SerializeEntries(IReadOnlyList<ReplicaEntry> entries) => Serialize(Batch(entries));

    /// <summary>Measures the exact standalone batch contract used by append admission.</summary>
    /// <param name="entries">Initialized ordered entry collection.</param>
    /// <returns>Exact complete native batch length.</returns>
    public static long MeasureEntries(IReadOnlyList<ReplicaEntry> entries) => Measure(Batch(entries));

    /// <summary>Validates the replica version prefix and returns its borrowed native payload.</summary>
    /// <param name="bytes">Complete encoded replica value.</param>
    /// <returns>The native payload within the supplied message.</returns>
    public static ReadOnlySpan<byte> PayloadBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < ReplicaProtocol.PayloadPrefixBytes
            || BinaryPrimitives.ReadUInt64LittleEndian(bytes) != ReplicaProtocol.PayloadMagic)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, ReplicaProtocol.UnsupportedFormat);
        }
        return bytes[ReplicaProtocol.PayloadPrefixBytes..];
    }

    private static ReplicaEntryBatch Batch(IReadOnlyList<ReplicaEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return new(entries is ImmutableArray<ReplicaEntry> array ? array : [.. entries]);
    }
}
