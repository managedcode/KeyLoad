using Microsoft.Extensions.Options;

namespace KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

/// <summary>Retains immutable-sized ZoneTree value chunks without per-record memory objects.</summary>
internal sealed class ScaledRawStorageValueArena
{
    private readonly int recordsPerChunk;
    private readonly int cancellationCheckInterval;
    private readonly byte[][] chunks;

    /// <summary>Generates each seeded value once into bounded contiguous chunks.</summary>
    public ScaledRawStorageValueArena(ScaledRawStorageCorpus corpus, IOptions<ScaledStorageExecutionOptions> executionOptions, CancellationToken cancellationToken = default)
    {
        const int RecordCountRecordsPerChunkStep = 1;

        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentNullException.ThrowIfNull(executionOptions);
        executionOptions.Value.Validate();
        cancellationCheckInterval = executionOptions.Value.CancellationCheckInterval;
        recordsPerChunk = executionOptions.Value.RecordsPerValueChunk;
        cancellationToken.ThrowIfCancellationRequested();
        RecordCount = corpus.RecordCount;
        ValueBytes = corpus.ValueBytes;
        RetainedValueBytes = (long)RecordCount * ValueBytes;
        chunks = new byte[(RecordCount + recordsPerChunk - RecordCountRecordsPerChunkStep) / recordsPerChunk][];
        FillChunks(corpus, cancellationToken);
    }

    /// <summary>Gets the number of retained record values.</summary>
    public int RecordCount { get; }

    /// <summary>Gets the byte length of each retained value.</summary>
    public int ValueBytes { get; }

    /// <summary>Gets bytes retained by immutable value chunks.</summary>
    public long RetainedValueBytes { get; }

    /// <summary>Returns the stable value slice for one seeded record.</summary>
    public Memory<byte> Value(int index)
    {
        if ((uint)index >= (uint)RecordCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        var chunkIndex = index / recordsPerChunk;
        var withinChunk = index % recordsPerChunk;
        return chunks[chunkIndex].AsMemory(checked(withinChunk * ValueBytes), ValueBytes);
    }

    private void FillChunks(ScaledRawStorageCorpus corpus, CancellationToken cancellationToken)
    {
        const int OperationInitialValue = 0;
        const int ChunkIndexInitialValue = 0;

        var scratch = GC.AllocateUninitializedArray<byte>(ValueBytes);
        var operation = OperationInitialValue;
        for (var chunkIndex = ChunkIndexInitialValue; chunkIndex < chunks.Length; chunkIndex++)
        {
            var firstIndex = chunkIndex * recordsPerChunk;
            var recordsInChunk = Math.Min(recordsPerChunk, RecordCount - firstIndex);
            var chunk = GC.AllocateUninitializedArray<byte>(checked(recordsInChunk * ValueBytes));
            chunks[chunkIndex] = chunk;
            FillChunk(corpus, scratch, chunk, firstIndex, recordsInChunk, ref operation, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private void FillChunk(ScaledRawStorageCorpus corpus, byte[] scratch, byte[] chunk,
        int firstIndex, int recordsInChunk, ref int operation, CancellationToken cancellationToken)
    {
        const int LocalIndexInitialValue = 0;
        const int EmptyOperationCancellationCheckStride = 0;

        for (var localIndex = LocalIndexInitialValue; localIndex < recordsInChunk; localIndex++)
        {
            if (operation++ % cancellationCheckInterval == EmptyOperationCancellationCheckStride)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var recordIndex = firstIndex + localIndex;
            corpus.WriteValue(recordIndex, scratch);
            scratch.AsSpan().CopyTo(chunk.AsSpan(localIndex * ValueBytes, ValueBytes));
        }
    }
}
