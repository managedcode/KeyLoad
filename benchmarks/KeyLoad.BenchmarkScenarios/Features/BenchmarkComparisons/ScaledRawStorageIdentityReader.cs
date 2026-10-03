using System.Buffers.Binary;
using System.Diagnostics;

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

internal static class ScaledRawStoragePreparationGuard
{
    private const string DeadlineMessage = "The scaled fixture preparation deadline expired.";

    internal static void Check(long deadlineStart, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (Stopwatch.GetElapsedTime(deadlineStart) >= ScaledRawStorageFixture.PreparationLimit)
        {
            throw new TimeoutException(DeadlineMessage);
        }
    }
}

internal static class ScaledRawStorageSeedRunner
{
    private const int CancellationCheckStride = 256;

    internal static long Run(ScaledRawStorageCorpus corpus, ScaledRawStorageZoneTreeEngine engine,
        byte[] scratch, int recordCount, long deadlineStart, ref long attempts,
        ref long successfulWrites, CancellationToken token)
    {
        var started = Stopwatch.GetTimestamp();
        for (var index = 0; index < recordCount; index++)
        {
            if (index % CancellationCheckStride == 0)
            {
                ScaledRawStoragePreparationGuard.Check(deadlineStart, token);
            }

            corpus.WriteValue(index, scratch);
            attempts++;
            engine.Upsert(index, scratch);
            successfulWrites++;
        }

        ScaledRawStoragePreparationGuard.Check(deadlineStart, token);
        return Stopwatch.GetTimestamp() - started;
    }
}
