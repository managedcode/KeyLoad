namespace KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

/// <summary>Retains immutable-sized ZoneTree value chunks without per-record memory objects.</summary>
internal sealed class ScaledRawStorageValueArena
{
    private const int RecordsPerChunk = 4096;
    private const int CancellationCheckStride = 256;
    private readonly byte[][] chunks;

    /// <summary>Generates each seeded value once into bounded contiguous chunks.</summary>
    public ScaledRawStorageValueArena(ScaledRawStorageCorpus corpus, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        cancellationToken.ThrowIfCancellationRequested();
        RecordCount = corpus.RecordCount;
        ValueBytes = corpus.ValueBytes;
        RetainedValueBytes = (long)RecordCount * ValueBytes;
        chunks = new byte[(RecordCount + RecordsPerChunk - 1) / RecordsPerChunk][];
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

        var chunkIndex = index / RecordsPerChunk;
        var withinChunk = index % RecordsPerChunk;
        return chunks[chunkIndex].AsMemory(checked(withinChunk * ValueBytes), ValueBytes);
    }

    private void FillChunks(ScaledRawStorageCorpus corpus, CancellationToken cancellationToken)
    {
        var scratch = GC.AllocateUninitializedArray<byte>(ValueBytes);
        var operation = 0;
        for (var chunkIndex = 0; chunkIndex < chunks.Length; chunkIndex++)
        {
            var firstIndex = chunkIndex * RecordsPerChunk;
            var recordsInChunk = Math.Min(RecordsPerChunk, RecordCount - firstIndex);
            var chunk = GC.AllocateUninitializedArray<byte>(checked(recordsInChunk * ValueBytes));
            chunks[chunkIndex] = chunk;
            FillChunk(corpus, scratch, chunk, firstIndex, recordsInChunk, ref operation, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private void FillChunk(ScaledRawStorageCorpus corpus, byte[] scratch, byte[] chunk,
        int firstIndex, int recordsInChunk, ref int operation, CancellationToken cancellationToken)
    {
        for (var localIndex = 0; localIndex < recordsInChunk; localIndex++)
        {
            if (operation++ % CancellationCheckStride == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var recordIndex = firstIndex + localIndex;
            corpus.WriteValue(recordIndex, scratch);
            scratch.AsSpan().CopyTo(chunk.AsSpan(localIndex * ValueBytes, ValueBytes));
        }
    }
}
