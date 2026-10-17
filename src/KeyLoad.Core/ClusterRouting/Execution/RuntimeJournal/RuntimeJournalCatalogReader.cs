using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Models;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ClusterRouting.Execution;

internal static class RuntimeJournalCatalogReader
{
    internal static ImmutableArray<RuntimeJournalHeaderV1> Read(IKeyValueView view,
        RuntimeJournalOptions options, CancellationToken cancellationToken)
    {
        var page = view.Scan(RuntimeJournalKeys.HeadersPrefix(),
            options.MaximumJournals + RuntimeJournalProtocol.CatalogHeaderProbeCount);
        if (page.HasMore || page.Records.Length > options.MaximumJournals)
        {
            Capacity();
        }
        var headers = ImmutableArray.CreateBuilder<RuntimeJournalHeaderV1>(page.Records.Length);
        var bytes = RuntimeJournalProtocol.EmptyLength;
        foreach (var row in page.Records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var header = Decode<RuntimeJournalHeaderV1>(row.Value.Span);
            ValidateHeader(header, options);
            if (!row.Key.Span.SequenceEqual(RuntimeJournalKeys.Header(header.Name)))
            {
                throw Errors.Fail(ErrorCode.RecoveryRequired, RuntimeJournalProtocol.InvalidState);
            }
            bytes = checked(bytes + header.Length);
            headers.Add(header);
        }
        ValidateNoOrphanChunks(view, headers, options, cancellationToken);
        var quota = RuntimeJournalRecordAccess.ReadRecord<RuntimeJournalQuotaV1>(view, RuntimeJournalKeys.Quota());
        if (quota is not { Version: RuntimeJournalProtocol.CurrentVersion }
            || quota.JournalCount != headers.Count || quota.TotalBytes != bytes || bytes > options.MaximumTotalBytes)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, RuntimeJournalProtocol.InvalidState);
        }
        return headers.MoveToImmutable();
    }

    private static void ValidateNoOrphanChunks(IKeyValueView view, ImmutableArray<RuntimeJournalHeaderV1>.Builder headers,
        RuntimeJournalOptions options, CancellationToken cancellationToken)
    {
        var possibleChunks = checked((int)((options.MaximumTotalBytes + options.ChunkBytes - RuntimeJournalProtocol.CeilingDivisionAdjustment) / options.ChunkBytes
            + options.MaximumJournals));
        var nextIndices = headers.ToDictionary(header => header.Name,
            static _ => RuntimeJournalProtocol.EmptyCount, StringComparer.Ordinal);
        var result = view.VisitRange(RuntimeJournalKeys.ChunksPrefix(), possibleChunks,
            (key, value) => ValidateChunkRow(key, value, headers, nextIndices, options), cancellationToken: cancellationToken);
        if (result.HasMore)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, RuntimeJournalProtocol.InvalidState);
        }
        foreach (var header in headers)
        {
            if (nextIndices[header.Name] != ChunkCount(header.Length, options.ChunkBytes))
            {
                throw Errors.Fail(ErrorCode.RecoveryRequired, RuntimeJournalProtocol.InvalidState);
            }
        }
    }

    private static bool ValidateChunkRow(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value,
        ImmutableArray<RuntimeJournalHeaderV1>.Builder headers, Dictionary<string, int> nextIndices,
        RuntimeJournalOptions options)
    {
        object?[] components;
        try
        {
            components = KeyCodec.Decode(key);
        }
        catch (KeyLoadException)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, RuntimeJournalProtocol.InvalidState);
        }
        if (components.Length != RuntimeJournalProtocol.ChunkKeyComponentCount
            || components[RuntimeJournalProtocol.ChunkKeySpaceComponent] is not string space || space != RuntimeJournalProtocol.Space
            || components[RuntimeJournalProtocol.ChunkKeyVersionComponent] is not long version || version != RuntimeJournalProtocol.CurrentVersion
            || components[RuntimeJournalProtocol.ChunkKeyKindComponent] is not string kind || kind != RuntimeJournalProtocol.ChunkKey
            || components[RuntimeJournalProtocol.ChunkKeyNameComponent] is not string name
            || components[RuntimeJournalProtocol.ChunkKeyIndexComponent] is not long index
            || index < RuntimeJournalProtocol.EmptyGeneration)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, RuntimeJournalProtocol.InvalidState);
        }
        var header = headers.FirstOrDefault(candidate => candidate.Name == name);
        if (header is null || index != nextIndices[name]
            || !key.SequenceEqual(RuntimeJournalKeys.Chunk(name, checked((int)index))))
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, RuntimeJournalProtocol.InvalidState);
        }
        var chunk = Decode<RuntimeJournalChunkV1>(value);
        var remaining = header.Length - index * options.ChunkBytes;
        var wanted = checked((int)Math.Min(options.ChunkBytes, remaining));
        if (chunk.Data is null || chunk.Version != RuntimeJournalProtocol.CurrentVersion
            || chunk.Index != index || chunk.Data.Length != wanted)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, RuntimeJournalProtocol.InvalidState);
        }
        nextIndices[name]++;
        return true;
    }

    private static void ValidateHeader(RuntimeJournalHeaderV1 header, RuntimeJournalOptions options)
    {
        if (header.Name is null || header.MetadataETag is null || header.Properties is null
            || header.Version != RuntimeJournalProtocol.CurrentVersion || header.InstanceId == Guid.Empty
            || header.OwnerGeneration < RuntimeJournalProtocol.InitialOwnerGeneration
            || header.ContentRevision < RuntimeJournalProtocol.EmptyRevision || header.Length < RuntimeJournalProtocol.EmptyLength
            || header.Length > options.MaximumJournalBytes
            || !RuntimeJournalValidation.IsValidMetadataETag(header.MetadataETag, header.InstanceId))
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, RuntimeJournalProtocol.InvalidState);
        }
        try
        {
            RuntimeJournalValidation.Name(header.Name, options);
            RuntimeJournalValidation.Metadata(header.Properties, options);
        }
        catch (KeyLoadException)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, RuntimeJournalProtocol.InvalidState);
        }
    }

    private static T Decode<T>(ReadOnlySpan<byte> bytes) where T : class
    {
        try
        {
            return NativeSerialization.Deserialize<T>(bytes)
                ?? throw Errors.Fail(ErrorCode.RecoveryRequired, RuntimeJournalProtocol.InvalidState);
        }
        catch (KeyLoadException)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, RuntimeJournalProtocol.InvalidState);
        }
    }

    private static int ChunkCount(long length, int chunkBytes)
        => checked((int)((length + chunkBytes - RuntimeJournalProtocol.CeilingDivisionAdjustment) / chunkBytes));

    private static void Capacity() => throw Errors.Fail(ErrorCode.ResourceExhausted, RuntimeJournalProtocol.Capacity);
}
