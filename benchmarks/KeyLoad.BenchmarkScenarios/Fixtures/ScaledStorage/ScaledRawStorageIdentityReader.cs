using System.Buffers.Binary;

namespace KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

internal static class ScaledRawStorageIdentityReader
{
    private const string MissingMessage = "A seeded scaled record is missing from the native engine.";
    private const string MismatchMessage = "The native value differs from the complete deterministic value.";

    internal static ulong Read(ScaledRawStorageCorpus corpus, ScaledRawStorageZoneTreeEngine engine,
        int index, ref long readCalls)
    {
        if ((uint)index > (uint)corpus.RecordCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        readCalls++;
        if (!engine.TryRead(index, out var value))
        {
            throw new InvalidOperationException(MissingMessage);
        }

        if (value.Length != corpus.ValueBytes)
        {
            throw new InvalidOperationException(MismatchMessage);
        }

        var identity = BinaryPrimitives.ReadUInt64BigEndian(value.Span);
        if (identity != (ulong)index)
        {
            throw new InvalidOperationException(MismatchMessage);
        }

        return identity;
    }
}
