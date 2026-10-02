using System.Buffers.Binary;
using System.Security.Cryptography;

namespace KeyLoad.Storage.ZoneTree;

internal readonly struct ZoneTreeCheckpointFrame
{
    private ZoneTreeCheckpointFrame(ulong magic, long position, byte[] header, byte[] payload)
    {
        Magic = magic;
        Position = position;
        Header = header;
        Payload = payload;
    }

    internal ulong Magic { get; }
    internal long Position { get; }
    internal byte[] Header { get; }
    internal byte[] Payload { get; }

    internal static ZoneTreeCheckpointFrame Read(FileStream input, ZoneTreeStoreOptions options)
    {
        if (input.Length - input.Position < ZoneTreePersistenceFormat.HeaderLength)
        {
            throw Errors.Fail(ErrorCode.Corruption, ZoneTreePersistenceFormat.CheckpointIncomplete);
        }

        var header = new byte[ZoneTreePersistenceFormat.HeaderLength];
        input.ReadExactly(header);
        var magic = BinaryPrimitives.ReadUInt64LittleEndian(header);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(ZoneTreePersistenceFormat.PayloadLengthOffset));
        var position = BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(ZoneTreePersistenceFormat.SequenceOffset));
        if (length <= 0 || length > options.MaxFrameBytes || input.Length - input.Position < length)
        {
            throw Errors.Fail(ErrorCode.Corruption, ZoneTreePersistenceFormat.CheckpointLengthInvalid);
        }

        var payload = new byte[length];
        input.ReadExactly(payload);
        VerifyChecksum(payload, header);
        return new(magic, position, header, payload);
    }

    internal static void Write(
        FileStream output,
        ZoneTreeStoreOptions options,
        ulong magic,
        long position,
        byte[] payload,
        IncrementalHash? digest)
    {
        if (payload.Length > options.MaxFrameBytes
            || output.Position + ZoneTreePersistenceFormat.HeaderLength + payload.Length > options.MaxSnapshotBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, ZoneTreePersistenceFormat.CheckpointBudgetExceeded);
        }

        var header = new byte[ZoneTreePersistenceFormat.HeaderLength];
        BinaryPrimitives.WriteUInt64LittleEndian(header, magic);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(ZoneTreePersistenceFormat.PayloadLengthOffset), payload.Length);
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(ZoneTreePersistenceFormat.SequenceOffset), position);
        SHA256.HashData(payload, header.AsSpan(ZoneTreePersistenceFormat.ChecksumOffset));
        output.Write(header);
        output.Write(payload);
        digest?.AppendData(header);
        digest?.AppendData(payload);
    }

    private static void VerifyChecksum(byte[] payload, byte[] header)
    {
        if (!CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(payload),
            header.AsSpan(ZoneTreePersistenceFormat.ChecksumOffset, ZoneTreePersistenceFormat.ChecksumLength)))
        {
            throw Errors.Fail(ErrorCode.Corruption, ZoneTreePersistenceFormat.CheckpointChecksumInvalid);
        }
    }
}
