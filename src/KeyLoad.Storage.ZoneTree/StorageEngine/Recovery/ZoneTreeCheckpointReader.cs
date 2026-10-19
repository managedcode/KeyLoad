using System.Security.Cryptography;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeCheckpointReader
{
    private const int InitialRecordCount = 0;
    private const int InitialAppliedPosition = 0;
    private const int InitialSnapshotPosition = 0;
    private const int EmptyRecordCount = 0;
    private const int EmptyKeyLength = 0;
    private const int EqualKeys = 0;

    internal static StorageSnapshot Read(FileStream input, ZoneTreeStoreOptions options,
        Action<StorageMutation>? apply = null, Action<StorageMutation>? validate = null,
        ZoneTreeCheckpointBudget? work = null)
    {
        if (input.Length > options.MaxSnapshotBytes && apply is null)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, ZoneTreePersistenceFormat.SnapshotBytesExceeded);
        }

        var budget = work ?? new ZoneTreeCheckpointBudget(options, TimeProvider.System, CancellationToken.None);
        var start = input.Position;
        var snapshot = ReadCore(input, budget, null, validate);
        var end = input.Position;
        if (apply is not null)
        {
            input.Position = start;
            ReadCore(input, budget, apply, null);
        }
        input.Position = end;
        return snapshot;
    }

    private static StorageSnapshot ReadCore(
        FileStream input,
        ZoneTreeCheckpointBudget budget,
        Action<StorageMutation>? apply = null,
        Action<StorageMutation>? validate = null)
    {
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        ZoneTreeCheckpointMetadata? metadata = null;
        long count = InitialRecordCount;
        long applied = InitialAppliedPosition;
        ReadOnlyMemory<byte>? previousKey = null;
        var appliedKey = KeyCodec.Encode(ZoneTreePersistenceFormat.SystemNamespace, ZoneTreePersistenceFormat.LastAppliedKey);
        while (true)
        {
            var frame = budget.ReadFrame(input);
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

                ApplyRecords(frame.Payload, appliedKey, apply, mutation =>
                {
                    budget.Charge(InitialRecordCount);
                    validate?.Invoke(mutation);
                }, ref count, ref applied, ref previousKey);
            }

            digest.AppendData(frame.Header);
            digest.AppendData(frame.Payload);
        }
    }

    internal static ZoneTreeCheckpointMetadata ReadMetadata(ZoneTreeCheckpointFrame frame)
    {
        if (frame.Magic != ZoneTreePersistenceFormat.CheckpointMagic)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, ZoneTreePersistenceFormat.CheckpointFormatUnsupported);
        }

        var metadata = OrleansSerialization.Deserialize<ZoneTreeCheckpointMetadata>(frame.Payload);
        if (metadata.Version != ZoneTreePersistenceFormat.CheckpointVersion || metadata.CodecVersion != KeyCodec.Version)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, ZoneTreePersistenceFormat.SnapshotFormatUnsupported);
        }

        if (metadata.Position < InitialSnapshotPosition || metadata.AppliedPosition < InitialSnapshotPosition || metadata.Incarnation == Guid.Empty
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
        Action<StorageMutation>? validate,
        ref long count,
        ref long applied,
        ref ReadOnlyMemory<byte>? previousKey)
    {
        var records = OrleansSerialization.Deserialize<StorageMutation[]>(payload);
        var lastKey = previousKey;
        var nextApplied = applied;
        if (records.Length == EmptyRecordCount)
        {
            throw Errors.Fail(ErrorCode.Corruption, ZoneTreePersistenceFormat.CheckpointRecordsInvalid);
        }
        foreach (var mutation in records)
        {
            if (mutation is null || mutation.Key.Length == EmptyKeyLength || mutation.Value is null
                || lastKey is { } priorKey && priorKey.Span.SequenceCompareTo(mutation.Key.Span) >= EqualKeys)
            {
                throw Errors.Fail(ErrorCode.Corruption, ZoneTreePersistenceFormat.CheckpointRecordsInvalid);
            }
            validate?.Invoke(mutation);
            lastKey = mutation.Key;
            if (mutation.Key.Span.SequenceEqual(appliedKey.Span))
            {
                nextApplied = OrleansSerialization.Deserialize<long>(mutation.Value.Value.Span);
                if (nextApplied < InitialAppliedPosition)
                {
                    throw Errors.Fail(ErrorCode.Corruption, ZoneTreePersistenceFormat.CheckpointRecordsInvalid);
                }
            }
        }
        var nextCount = checked(count + records.LongLength);
        foreach (var mutation in records)
        {
            apply?.Invoke(mutation);
        }
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
        var footer = OrleansSerialization.Deserialize<ZoneTreeCheckpointFooter>(payload);
        if (footer.Records != count || metadata.AppliedPosition != applied
            || footer.Checksum != Convert.ToHexStringLower(digest.GetHashAndReset()))
        {
            throw Errors.Fail(ErrorCode.Corruption, ZoneTreePersistenceFormat.CheckpointVerificationFailed);
        }

        return new(metadata.Incarnation, metadata.Position, metadata.AppliedPosition, count);
    }
}
