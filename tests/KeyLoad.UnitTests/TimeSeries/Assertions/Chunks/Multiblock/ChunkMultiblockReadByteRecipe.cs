using KeyLoad.Core.Features.TimeSeries;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed record ChunkMultiblockReadByteRecipe(long FirstBytes, long TotalBytes)
{
    private const string CountMismatch = "The acknowledged multi-block sample count differs from the literal corpus.";
    private const string MissingBlock = "The acknowledged selected block is absent.";
    private const long NoReadBytes = 0;
    private const int FirstOrdinal = 0;
    private const int SecondOrdinal = 1;

    internal static ChunkMultiblockReadByteRecipe Capture(ChunkSelectedIntegrityFixture fixture, bool healthy)
        => fixture.Store.Read<ChunkMultiblockReadByteRecipe>(view =>
        {
            var generation = healthy ? ChunkMultiblockBoundaryData.HealthyGeneration : ChunkMultiblockBoundaryData.InitialGeneration;
            var first = Block(view, fixture, generation, FirstOrdinal, SampleChunkWire.MaximumRecords);
            var second = Block(view, fixture, generation, SecondOrdinal,
                (healthy ? ChunkMultiblockBoundaryData.HealthyCount : ChunkMultiblockBoundaryData.InitialCount) - SampleChunkWire.MaximumRecords);
            return new(first, checked(first + second));
        });

    private static long Block(IKeyValueView view, ChunkSelectedIntegrityFixture fixture, long generation,
        int ordinal, int expectedCount)
    {
        var key = SampleChunkKeys.Block(fixture.Partition, SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, fixture.WindowId, generation, ordinal);
        var bytes = NoReadBytes;
        var present = view.ReadValue(key, value =>
        {
            var payload = NativeSerialization.Deserialize<SampleChunkPayload>(value);
            if (payload.RecordCount != expectedCount)
            {
                throw new InvalidOperationException(CountMismatch);
            }
            bytes = checked((long)key.Length + value.Length);
        });
        if (!present)
        {
            throw new InvalidOperationException(MissingBlock);
        }
        return bytes;
    }
}
