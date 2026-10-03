using System.Security.Cryptography;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeCheckpointReader
{
    internal static StorageSnapshot Read(
        FileStream input,
        ZoneTreeStoreOptions options,
        Action<StorageMutation>? apply = null)
        => Read(input, options, apply, CurrentFormat);

    internal static StorageSnapshot ReadNative3ForUpgrade(
        FileStream input,
        ZoneTreeStoreOptions options,
        Action<StorageMutation> apply)
        => Read(input, options, apply, SourceFormat);

    private static StorageSnapshot Read(
        FileStream input,
        ZoneTreeStoreOptions options,
        Action<StorageMutation>? apply,
        CheckpointFormat format)
    {
        if (input.Length > options.MaxSnapshotBytes && apply is null)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, ZoneTreePersistenceFormat.SnapshotBytesExceeded);
        }

        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        ZoneTreeCheckpointMetadata? metadata = null;
        long count = 0;
        long applied = 0;
        ReadOnlyMemory<byte>? previousKey = null;
        var appliedKey = KeyCodec.Encode(ZoneTreePersistenceFormat.SystemNamespace, ZoneTreePersistenceFormat.LastAppliedKey);
        while (true)
        {
            var maximumPosition = format == SourceFormat ? options.MaxSnapshotBytes : long.MaxValue;
            var frame = ZoneTreeCheckpointFrame.Read(input, options, maximumPosition);
            if (metadata is null)
            {
                metadata = ReadMetadata(frame, format);
            }
            else
            {
                if (frame.Position != metadata.Position)
                {
                    throw Errors.Fail(ErrorCode.Corruption, ZoneTreePersistenceFormat.CheckpointCutsMismatch);
                }

                if (frame.Magic == format.EndMagic)
                {
                    return ReadFooter(frame.Payload, metadata, count, applied, digest);
                }

                if (frame.Magic != format.DataMagic)
                {
                    throw Errors.Fail(ErrorCode.Corruption, ZoneTreePersistenceFormat.CheckpointFrameTypeInvalid);
                }

                ApplyRecords(frame.Payload, appliedKey, apply, ref count, ref applied, ref previousKey);
            }

            digest.AppendData(frame.Header);
            digest.AppendData(frame.Payload);
        }
    }

    private static ZoneTreeCheckpointMetadata ReadMetadata(ZoneTreeCheckpointFrame frame, CheckpointFormat format)
    {
        if (frame.Magic != format.HeaderMagic)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, ZoneTreePersistenceFormat.CheckpointFormatUnsupported);
        }

        var metadata = NativeSerialization.Deserialize<ZoneTreeCheckpointMetadata>(frame.Payload);
        if (metadata.Version != format.Version || metadata.CodecVersion != KeyCodec.Version)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, ZoneTreePersistenceFormat.SnapshotFormatUnsupported);
        }

        if (metadata.Position < 0 || metadata.AppliedPosition < 0 || metadata.Incarnation == Guid.Empty
            || frame.Position != metadata.Position)
        {
            throw Errors.Fail(ErrorCode.Corruption, ZoneTreePersistenceFormat.CheckpointCutInvalid);
        }

        return metadata;
    }

    private static CheckpointFormat CurrentFormat => new(ZoneTreePersistenceFormat.CheckpointVersion,
        ZoneTreePersistenceFormat.CheckpointMagic, ZoneTreePersistenceFormat.CheckpointDataMagic,
        ZoneTreePersistenceFormat.CheckpointEndMagic);

    private static CheckpointFormat SourceFormat => new(ZoneTreePersistenceFormat.SourceCheckpointVersion,
        ZoneTreePersistenceFormat.SourceCheckpointMagic, ZoneTreePersistenceFormat.SourceCheckpointDataMagic,
        ZoneTreePersistenceFormat.SourceCheckpointEndMagic);

    private readonly record struct CheckpointFormat(int Version, ulong HeaderMagic, ulong DataMagic, ulong EndMagic);

    private static void ApplyRecords(
        byte[] payload,
        ReadOnlyMemory<byte> appliedKey,
        Action<StorageMutation>? apply,
        ref long count,
        ref long applied,
        ref ReadOnlyMemory<byte>? previousKey)
    {
        var records = NativeSerialization.Deserialize<StorageMutation[]>(payload);
        var lastKey = previousKey;
        var nextApplied = applied;
        if (records.Length == 0)
        { throw Errors.Fail(ErrorCode.Corruption, ZoneTreePersistenceFormat.CheckpointRecordsInvalid); }
        foreach (var mutation in records)
        {
            if (mutation is null || mutation.Key.Length == 0 || mutation.Value is null
                || lastKey is { } priorKey && priorKey.Span.SequenceCompareTo(mutation.Key.Span) >= 0)
            { throw Errors.Fail(ErrorCode.Corruption, ZoneTreePersistenceFormat.CheckpointRecordsInvalid); }
            lastKey = mutation.Key;
            if (mutation.Key.Span.SequenceEqual(appliedKey.Span))
            {
                nextApplied = NativeSerialization.Deserialize<long>(mutation.Value.Value.Span);
                if (nextApplied < 0)
                { throw Errors.Fail(ErrorCode.Corruption, ZoneTreePersistenceFormat.CheckpointRecordsInvalid); }
            }
        }
        var nextCount = checked(count + records.LongLength);
        foreach (var mutation in records)
        { apply?.Invoke(mutation); }
        previousKey = lastKey;
        applied = nextApplied;
        count = nextCount;
    }

    private static StorageSnapshot ReadFooter(
        byte[] payload,
        ZoneTreeCheckpointMetadata metadata,
        long count,
        long applied,
        IncrementalHash digest)
    {
        var footer = NativeSerialization.Deserialize<ZoneTreeCheckpointFooter>(payload);
        if (footer.Records != count || metadata.AppliedPosition != applied
            || footer.Checksum != Convert.ToHexStringLower(digest.GetHashAndReset()))
        {
            throw Errors.Fail(ErrorCode.Corruption, ZoneTreePersistenceFormat.CheckpointVerificationFailed);
        }

        return new(metadata.Incarnation, metadata.Position, metadata.AppliedPosition, count);
    }
}
