using System.Security.Cryptography;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeCheckpointReader
{
    internal static StorageSnapshot Read(
        FileStream input,
        ZoneTreeStoreOptions options,
        Action<StorageMutation>? apply = null)
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
            var frame = ZoneTreeCheckpointFrame.Read(input, options);
            if (metadata is null)
            {
                metadata = ReadMetadata(frame);
            }
            else
            {
                if (frame.Position != metadata.Position)
                {
                    throw Errors.Fail(ErrorCode.Corruption, ZoneTreePersistenceFormat.CheckpointCutsMismatch);
                }

                if (frame.Magic == ZoneTreePersistenceFormat.CheckpointEndMagic)
                {
                    return ReadFooter(frame.Payload, metadata, count, applied, digest);
                }

                if (frame.Magic != ZoneTreePersistenceFormat.CheckpointDataMagic)
                {
                    throw Errors.Fail(ErrorCode.Corruption, ZoneTreePersistenceFormat.CheckpointFrameTypeInvalid);
                }

                ApplyRecords(frame.Payload, appliedKey, apply, ref count, ref applied, ref previousKey);
            }

            digest.AppendData(frame.Header);
            digest.AppendData(frame.Payload);
        }
    }

    private static ZoneTreeCheckpointMetadata ReadMetadata(ZoneTreeCheckpointFrame frame)
    {
        if (frame.Magic != ZoneTreePersistenceFormat.CheckpointMagic)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, ZoneTreePersistenceFormat.CheckpointFormatUnsupported);
        }

        var metadata = JsonDefaults.Deserialize<ZoneTreeCheckpointMetadata>(frame.Payload);
        if (metadata.Version != ZoneTreePersistenceFormat.CheckpointVersion || metadata.CodecVersion != KeyCodec.Version)
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

    private static void ApplyRecords(
        byte[] payload,
        ReadOnlyMemory<byte> appliedKey,
        Action<StorageMutation>? apply,
        ref long count,
        ref long applied,
        ref ReadOnlyMemory<byte>? previousKey)
    {
        foreach (var mutation in JsonDefaults.Deserialize<StorageMutation[]>(payload))
        {
            if (mutation.Key.Length == 0 || mutation.Value is null
                || previousKey is { } priorKey && priorKey.Span.SequenceCompareTo(mutation.Key.Span) >= 0)
            {
                throw Errors.Fail(ErrorCode.Corruption, ZoneTreePersistenceFormat.CheckpointRecordsInvalid);
            }

            previousKey = mutation.Key;
            if (mutation.Key.Span.SequenceEqual(appliedKey.Span))
            {
                applied = JsonDefaults.Deserialize<long>(mutation.Value.Value.Span);
            }

            count++;
            apply?.Invoke(mutation);
        }
    }

    private static StorageSnapshot ReadFooter(
        byte[] payload,
        ZoneTreeCheckpointMetadata metadata,
        long count,
        long applied,
        IncrementalHash digest)
    {
        var footer = JsonDefaults.Deserialize<ZoneTreeCheckpointFooter>(payload);
        if (footer.Records != count || metadata.AppliedPosition != applied
            || footer.Checksum != Convert.ToHexStringLower(digest.GetHashAndReset()))
        {
            throw Errors.Fail(ErrorCode.Corruption, ZoneTreePersistenceFormat.CheckpointVerificationFailed);
        }

        return new(metadata.Incarnation, metadata.Position, metadata.AppliedPosition, count);
    }
}
