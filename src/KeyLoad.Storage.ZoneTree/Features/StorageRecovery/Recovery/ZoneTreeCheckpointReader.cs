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

    internal static StorageSnapshot Read(
        FileStream input,
        ZoneTreeStoreOptions options,
        Action<StorageMutation>? apply = null,
        Action<StorageMutation>? validate = null)
        => Read(input, options, apply, CurrentFormat, validate: validate);

    internal static StorageSnapshot ReadNative3ForUpgrade(
        FileStream input,
        ZoneTreeStoreOptions options,
        Action<StorageMutation> apply,
        Guid expectedIncarnation)
        => Read(input, options, apply, SourceFormat, expectedIncarnation);

    internal static StorageSnapshot ReadNative4ForUpgrade(
        FileStream input,
        ZoneTreeStoreOptions options,
        Action<StorageMutation> apply,
        Guid expectedIncarnation)
        => Read(input, options, apply, Native6SourceFormat, expectedIncarnation);

    private static StorageSnapshot Read(
        FileStream input,
        ZoneTreeStoreOptions options,
        Action<StorageMutation>? apply,
        CheckpointFormat format,
        Guid? expectedIncarnation = null,
        Action<StorageMutation>? validate = null)
    {
        if (input.Length > options.MaxSnapshotBytes && apply is null)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, ZoneTreePersistenceFormat.SnapshotBytesExceeded);
        }

        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        ZoneTreeCheckpointMetadata? metadata = null;
        long count = InitialRecordCount;
        long applied = InitialAppliedPosition;
        ReadOnlyMemory<byte>? previousKey = null;
        var appliedKey = KeyCodec.Encode(ZoneTreePersistenceFormat.SystemNamespace, ZoneTreePersistenceFormat.LastAppliedKey);
        while (true)
        {
            var maximumPosition = format == CurrentFormat ? long.MaxValue : options.MaxSnapshotBytes;
            var frame = ZoneTreeCheckpointFrame.Read(input, options, maximumPosition);
            if (metadata is null)
            {
                metadata = ReadMetadata(frame, format, expectedIncarnation);
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

                ApplyRecords(frame.Payload, appliedKey, apply, validate, ref count, ref applied, ref previousKey);
            }

            digest.AppendData(frame.Header);
            digest.AppendData(frame.Payload);
        }
    }

    private static ZoneTreeCheckpointMetadata ReadMetadata(ZoneTreeCheckpointFrame frame, CheckpointFormat format,
        Guid? expectedIncarnation)
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

        if (metadata.Position < InitialSnapshotPosition || metadata.AppliedPosition < InitialSnapshotPosition || metadata.Incarnation == Guid.Empty
            || frame.Position != metadata.Position)
        {
            throw Errors.Fail(ErrorCode.Corruption, ZoneTreePersistenceFormat.CheckpointCutInvalid);
        }
        if (expectedIncarnation is { } expected && metadata.Incarnation != expected)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ZoneTreePersistenceFormat.SnapshotScopeInvalid);
        }

        return metadata;
    }

    private static CheckpointFormat CurrentFormat => new(ZoneTreePersistenceFormat.CheckpointVersion,
        ZoneTreePersistenceFormat.CheckpointMagic, ZoneTreePersistenceFormat.CheckpointDataMagic,
        ZoneTreePersistenceFormat.CheckpointEndMagic);

    private static CheckpointFormat SourceFormat => new(ZoneTreePersistenceFormat.Native5CheckpointVersion,
        ZoneTreePersistenceFormat.SourceCheckpointMagic, ZoneTreePersistenceFormat.SourceCheckpointDataMagic,
        ZoneTreePersistenceFormat.SourceCheckpointEndMagic);

    private static CheckpointFormat Native6SourceFormat => new(ZoneTreePersistenceFormat.Native6CheckpointVersion,
        ZoneTreePersistenceFormat.Native6CheckpointMagic, ZoneTreePersistenceFormat.Native6CheckpointDataMagic,
        ZoneTreePersistenceFormat.Native6CheckpointEndMagic);

    private readonly record struct CheckpointFormat(int Version, ulong HeaderMagic, ulong DataMagic, ulong EndMagic);

    private static void ApplyRecords(
        byte[] payload,
        ReadOnlyMemory<byte> appliedKey,
        Action<StorageMutation>? apply,
        Action<StorageMutation>? validate,
        ref long count,
        ref long applied,
        ref ReadOnlyMemory<byte>? previousKey)
    {
        var records = NativeSerialization.Deserialize<StorageMutation[]>(payload);
        var lastKey = previousKey;
        var nextApplied = applied;
        if (records.Length == EmptyRecordCount)
        { throw Errors.Fail(ErrorCode.Corruption, ZoneTreePersistenceFormat.CheckpointRecordsInvalid); }
        foreach (var mutation in records)
        {
            if (mutation is null || mutation.Key.Length == EmptyKeyLength || mutation.Value is null
                || lastKey is { } priorKey && priorKey.Span.SequenceCompareTo(mutation.Key.Span) >= EqualKeys)
            { throw Errors.Fail(ErrorCode.Corruption, ZoneTreePersistenceFormat.CheckpointRecordsInvalid); }
            validate?.Invoke(mutation);
            lastKey = mutation.Key;
            if (mutation.Key.Span.SequenceEqual(appliedKey.Span))
            {
                nextApplied = NativeSerialization.Deserialize<long>(mutation.Value.Value.Span);
                if (nextApplied < InitialAppliedPosition)
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
