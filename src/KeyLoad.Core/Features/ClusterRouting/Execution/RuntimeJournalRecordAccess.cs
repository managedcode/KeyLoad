using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Models;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ClusterRouting.Execution;

internal static class RuntimeJournalRecordAccess
{
    internal static ImmutableArray<RuntimeJournalHeaderV1> ReadCatalog(IKeyValueView view,
        RuntimeJournalOptions options, CancellationToken cancellationToken)
    {
        var page = view.Scan(RuntimeJournalKeys.HeadersPrefix(), options.MaximumJournals + 1);
        if (page.HasMore || page.Records.Length > options.MaximumJournals) Capacity();
        var headers = ImmutableArray.CreateBuilder<RuntimeJournalHeaderV1>(page.Records.Length);
        long bytes = 0;
        foreach (var row in page.Records)
        {
            cancellationToken.ThrowIfCancellationRequested();
        var header = Decode<RuntimeJournalHeaderV1>(row.Value.Span);
            if (!row.Key.Span.SequenceEqual(RuntimeJournalKeys.Header(header.Name)))
                throw Errors.Fail(ErrorCode.RecoveryRequired, RuntimeJournalProtocol.InvalidState);
            ValidateHeader(header, options);
            bytes = checked(bytes + header.Length);
            headers.Add(header);
        }
        ValidateNoOrphanChunks(view, headers, options, cancellationToken);
        var quota = ReadRecord<RuntimeJournalQuotaV1>(view, RuntimeJournalKeys.Quota());
        if (quota is not { Version: RuntimeJournalProtocol.CurrentVersion }
            || quota.JournalCount != headers.Count || quota.TotalBytes != bytes || bytes > options.MaximumTotalBytes)
            throw Errors.Fail(ErrorCode.RecoveryRequired, RuntimeJournalProtocol.InvalidState);
        return headers.MoveToImmutable();
    }

    internal static RuntimeJournalHeaderV1? FindHeader(IKeyValueView view, string name)
        => ReadRecord<RuntimeJournalHeaderV1>(view, RuntimeJournalKeys.Header(name));

    internal static RuntimeJournalPage ReadPage(IKeyValueView view, RuntimeJournalHeaderV1 header,
        long offset, int maximumPageBytes, CancellationToken cancellationToken)
    {
        if (offset < 0 || offset > header.Length)
            throw Errors.Fail(ErrorCode.Validation, RuntimeJournalProtocol.InvalidRequest);
        var requested = (int)Math.Min(maximumPageBytes, header.Length - offset);
        var data = new byte[requested];
        var copied = 0;
        while (copied < requested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var absolute = offset + copied;
            var chunkIndex = checked((int)(absolute / maximumPageBytes));
            var inChunk = checked((int)(absolute % maximumPageBytes));
            var chunk = RequireChunk(view, header.Name, chunkIndex, maximumPageBytes);
            var amount = Math.Min(requested - copied, chunk.Data.Length - inChunk);
            if (amount <= 0) throw Errors.Fail(ErrorCode.RecoveryRequired, RuntimeJournalProtocol.InvalidState);
            chunk.Data.AsSpan(inChunk, amount).CopyTo(data.AsSpan(copied));
            copied += amount;
        }
        return new RuntimeJournalPage(data, offset + requested == header.Length);
    }

    internal static byte[] ReadAll(IKeyValueView view, RuntimeJournalHeaderV1 header, RuntimeJournalOptions options,
        CancellationToken cancellationToken = default)
    {
        var output = new byte[checked((int)header.Length)];
        var offset = 0;
        while (offset < output.Length)
        {
            var page = ReadPage(view, header, offset, options.ChunkBytes, cancellationToken);
            page.Data.Span.CopyTo(output.AsSpan(offset));
            offset += page.Data.Length;
        }
        return output;
    }

    internal static void WriteBody(IAtomicTransaction transaction, string name, ReadOnlySpan<byte> data,
        RuntimeJournalOptions options, long previousLength)
    {
        var previousChunks = ChunkCount(previousLength, options.ChunkBytes);
        var nextChunks = ChunkCount(data.Length, options.ChunkBytes);
        for (var index = 0; index < nextChunks; index++)
        {
            var offset = index * options.ChunkBytes;
            var length = Math.Min(options.ChunkBytes, data.Length - offset);
            transaction.PutRecord(RuntimeJournalKeys.Chunk(name, index),
                new RuntimeJournalChunkV1(RuntimeJournalProtocol.CurrentVersion, index, data.Slice(offset, length).ToArray()));
        }
        for (var index = nextChunks; index < previousChunks; index++) transaction.Delete(RuntimeJournalKeys.Chunk(name, index));
    }

    internal static void DeleteBody(IAtomicTransaction transaction, string name, long length, int chunkBytes)
    {
        for (var index = 0; index < ChunkCount(length, chunkBytes); index++) transaction.Delete(RuntimeJournalKeys.Chunk(name, index));
    }

    internal static void RequireNoRows(IKeyValueView view)
    {
        if (view.Scan(RuntimeJournalKeys.HeadersPrefix(), 1).Records.Length != 0
            || view.Scan(RuntimeJournalKeys.ChunksPrefix(), 1).Records.Length != 0)
            throw Errors.Fail(ErrorCode.RecoveryRequired, RuntimeJournalProtocol.InvalidState);
    }

    internal static RuntimeJournalQuotaV1 RequireQuota(IKeyValueView view)
        => ReadRecord<RuntimeJournalQuotaV1>(view, RuntimeJournalKeys.Quota())
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, RuntimeJournalProtocol.InvalidState);

    internal static void UpdateQuota(IAtomicTransaction transaction, RuntimeJournalQuotaV1 quota,
        int journalDelta, long byteDelta, RuntimeJournalOptions options)
    {
        var count = checked(quota.JournalCount + journalDelta);
        var bytes = checked(quota.TotalBytes + byteDelta);
        if (count < 0 || count > options.MaximumJournals || bytes < 0 || bytes > options.MaximumTotalBytes)
            Capacity();
        transaction.PutRecord(RuntimeJournalKeys.Quota(), quota with { JournalCount = count, TotalBytes = bytes });
    }

    private static void ValidateNoOrphanChunks(IKeyValueView view, ImmutableArray<RuntimeJournalHeaderV1>.Builder headers,
        RuntimeJournalOptions options, CancellationToken cancellationToken)
    {
        var possibleChunks = checked((int)((options.MaximumTotalBytes + options.ChunkBytes - 1) / options.ChunkBytes
            + options.MaximumJournals));
        var nextIndices = headers.ToDictionary(header => header.Name, static _ => 0, StringComparer.Ordinal);
        var result = view.VisitRange(RuntimeJournalKeys.ChunksPrefix(), possibleChunks,
            (key, value) => ValidateChunkRow(key, value, headers, nextIndices, options), cancellationToken: cancellationToken);
        if (result.HasMore)
            throw Errors.Fail(ErrorCode.RecoveryRequired, RuntimeJournalProtocol.InvalidState);
        foreach (var header in headers)
            if (nextIndices[header.Name] != ChunkCount(header.Length, options.ChunkBytes))
                throw Errors.Fail(ErrorCode.RecoveryRequired, RuntimeJournalProtocol.InvalidState);
    }

    private static bool ValidateChunkRow(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value,
        ImmutableArray<RuntimeJournalHeaderV1>.Builder headers, Dictionary<string, int> nextIndices,
        RuntimeJournalOptions options)
    {
        object?[] components;
        try { components = KeyCodec.Decode(key); }
        catch (KeyLoadException) { throw Errors.Fail(ErrorCode.RecoveryRequired, RuntimeJournalProtocol.InvalidState); }
        if (components.Length != 5 || components[0] is not string space || space != RuntimeJournalProtocol.Space
            || components[1] is not long version || version != RuntimeJournalProtocol.CurrentVersion
            || components[2] is not string kind || kind != RuntimeJournalProtocol.ChunkKey
            || components[3] is not string name || components[4] is not long index || index < 0)
            throw Errors.Fail(ErrorCode.RecoveryRequired, RuntimeJournalProtocol.InvalidState);
        var header = headers.FirstOrDefault(candidate => candidate.Name == name);
        if (header is null || index != nextIndices[name]
            || !key.SequenceEqual(RuntimeJournalKeys.Chunk(name, checked((int)index))))
            throw Errors.Fail(ErrorCode.RecoveryRequired, RuntimeJournalProtocol.InvalidState);
        var chunk = Decode<RuntimeJournalChunkV1>(value);
        var remaining = header.Length - index * options.ChunkBytes;
        var wanted = checked((int)Math.Min(options.ChunkBytes, remaining));
        if (chunk.Data is null || chunk.Version != RuntimeJournalProtocol.CurrentVersion
            || chunk.Index != index || chunk.Data.Length != wanted)
            throw Errors.Fail(ErrorCode.RecoveryRequired, RuntimeJournalProtocol.InvalidState);
        nextIndices[name]++;
        return true;
    }

    private static RuntimeJournalChunkV1 RequireChunk(IKeyValueView view, string name, int index, int chunkBytes)
    {
        var chunk = ReadRecord<RuntimeJournalChunkV1>(view, RuntimeJournalKeys.Chunk(name, index));
        if (chunk is null || chunk.Data is null || chunk.Version != RuntimeJournalProtocol.CurrentVersion || chunk.Index != index
            || chunk.Data.Length is 0 || chunk.Data.Length > chunkBytes)
            throw Errors.Fail(ErrorCode.RecoveryRequired, RuntimeJournalProtocol.InvalidState);
        return chunk;
    }

    private static void ValidateHeader(RuntimeJournalHeaderV1 header, RuntimeJournalOptions options)
    {
        if (header.Name is null || header.MetadataETag is null || header.Properties is null
            || header.Version != RuntimeJournalProtocol.CurrentVersion || header.InstanceId == Guid.Empty
            || header.OwnerGeneration < 1 || header.ContentRevision < 0 || header.Length < 0
            || header.Length > options.MaximumJournalBytes || string.IsNullOrWhiteSpace(header.MetadataETag))
            throw Errors.Fail(ErrorCode.RecoveryRequired, RuntimeJournalProtocol.InvalidState);
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
        try { return NativeSerialization.Deserialize<T>(bytes) ?? throw Errors.Fail(ErrorCode.RecoveryRequired,
            RuntimeJournalProtocol.InvalidState); }
        catch (KeyLoadException) { throw Errors.Fail(ErrorCode.RecoveryRequired, RuntimeJournalProtocol.InvalidState); }
    }

    internal static T? ReadRecord<T>(IKeyValueView view, byte[] key) where T : class
    {
        try { return view.GetRecord<T>(key); }
        catch (KeyLoadException) { throw Errors.Fail(ErrorCode.RecoveryRequired, RuntimeJournalProtocol.InvalidState); }
    }

    private static int ChunkCount(long length, int chunkBytes)
        => checked((int)((length + chunkBytes - 1) / chunkBytes));

    private static void Capacity() => throw Errors.Fail(ErrorCode.ResourceExhausted, RuntimeJournalProtocol.Capacity);
}
