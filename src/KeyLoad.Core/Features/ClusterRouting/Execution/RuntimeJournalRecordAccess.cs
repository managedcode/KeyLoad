using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Models;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ClusterRouting.Execution;

internal static class RuntimeJournalRecordAccess
{
    internal static ImmutableArray<RuntimeJournalHeaderV1> ReadCatalog(IKeyValueView view,
        RuntimeJournalOptions options, CancellationToken cancellationToken)
        => RuntimeJournalCatalogReader.Read(view, options, cancellationToken);

    internal static RuntimeJournalHeaderV1? FindHeader(IKeyValueView view, string name)
        => ReadRecord<RuntimeJournalHeaderV1>(view, RuntimeJournalKeys.Header(name));

    internal static RuntimeJournalPage ReadPage(IKeyValueView view, RuntimeJournalHeaderV1 header,
        long offset, int maximumPageBytes, CancellationToken cancellationToken)
    {
        if (offset < RuntimeJournalProtocol.EmptyLength || offset > header.Length)
        {
            throw Errors.Fail(ErrorCode.Validation, RuntimeJournalProtocol.InvalidRequest);
        }
        var requested = (int)Math.Min(maximumPageBytes, header.Length - offset);
        var data = new byte[requested];
        var copied = RuntimeJournalProtocol.EmptyCount;
        while (copied < requested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var absolute = offset + copied;
            var chunkIndex = checked((int)(absolute / maximumPageBytes));
            var inChunk = checked((int)(absolute % maximumPageBytes));
            var chunk = RequireChunk(view, header.Name, chunkIndex, maximumPageBytes);
            var amount = Math.Min(requested - copied, chunk.Data.Length - inChunk);
            if (amount <= RuntimeJournalProtocol.EmptyCount)
            {
                throw Errors.Fail(ErrorCode.RecoveryRequired, RuntimeJournalProtocol.InvalidState);
            }
            chunk.Data.AsSpan(inChunk, amount).CopyTo(data.AsSpan(copied));
            copied += amount;
        }
        return new RuntimeJournalPage(data, offset + requested == header.Length);
    }

    internal static byte[] ReadAll(IKeyValueView view, RuntimeJournalHeaderV1 header, RuntimeJournalOptions options,
        CancellationToken cancellationToken = default)
    {
        var output = new byte[checked((int)header.Length)];
        var offset = RuntimeJournalProtocol.EmptyCount;
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
        const int IndexInitialValue = 0;

        var previousChunks = ChunkCount(previousLength, options.ChunkBytes);
        var nextChunks = ChunkCount(data.Length, options.ChunkBytes);
        for (var index = IndexInitialValue; index < nextChunks; index++)
        {
            var offset = index * options.ChunkBytes;
            var length = Math.Min(options.ChunkBytes, data.Length - offset);
            transaction.PutRecord(RuntimeJournalKeys.Chunk(name, index),
                new RuntimeJournalChunkV1(RuntimeJournalProtocol.CurrentVersion, index, data.Slice(offset, length).ToArray()));
        }
        for (var index = nextChunks; index < previousChunks; index++)
        {
            transaction.Delete(RuntimeJournalKeys.Chunk(name, index));
        }
    }

    internal static void DeleteBody(IAtomicTransaction transaction, string name, long length, int chunkBytes)
    {
        const int IndexInitialValue = 0;

        for (var index = IndexInitialValue; index < ChunkCount(length, chunkBytes); index++)
        {
            transaction.Delete(RuntimeJournalKeys.Chunk(name, index));
        }
    }

    internal static void RequireNoRows(IKeyValueView view)
    {
        if (view.Scan(RuntimeJournalKeys.HeadersPrefix(), RuntimeJournalProtocol.CatalogHeaderProbeCount).Records.Length
                != RuntimeJournalProtocol.EmptyCount
            || view.Scan(RuntimeJournalKeys.ChunksPrefix(), RuntimeJournalProtocol.CatalogHeaderProbeCount).Records.Length
                != RuntimeJournalProtocol.EmptyCount)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, RuntimeJournalProtocol.InvalidState);
        }
    }

    internal static RuntimeJournalQuotaV1 RequireQuota(IKeyValueView view)
        => ReadRecord<RuntimeJournalQuotaV1>(view, RuntimeJournalKeys.Quota())
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, RuntimeJournalProtocol.InvalidState);

    internal static void UpdateQuota(IAtomicTransaction transaction, RuntimeJournalQuotaV1 quota,
        int journalDelta, long byteDelta, RuntimeJournalOptions options)
    {
        var count = checked(quota.JournalCount + journalDelta);
        var bytes = checked(quota.TotalBytes + byteDelta);
        if (count < RuntimeJournalProtocol.EmptyCount || count > options.MaximumJournals
            || bytes < RuntimeJournalProtocol.EmptyLength || bytes > options.MaximumTotalBytes)
        {
            Capacity();
        }
        transaction.PutRecord(RuntimeJournalKeys.Quota(), quota with { JournalCount = count, TotalBytes = bytes });
    }

    private static RuntimeJournalChunkV1 RequireChunk(IKeyValueView view, string name, int index, int chunkBytes)
    {
        var chunk = ReadRecord<RuntimeJournalChunkV1>(view, RuntimeJournalKeys.Chunk(name, index));
        if (chunk is null || chunk.Data is null || chunk.Version != RuntimeJournalProtocol.CurrentVersion || chunk.Index != index
            || chunk.Data.Length == RuntimeJournalProtocol.EmptyCount || chunk.Data.Length > chunkBytes)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, RuntimeJournalProtocol.InvalidState);
        }
        return chunk;
    }

    internal static T? ReadRecord<T>(IKeyValueView view, byte[] key) where T : class
    {
        try
        {
            return view.GetRecord<T>(key);
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
