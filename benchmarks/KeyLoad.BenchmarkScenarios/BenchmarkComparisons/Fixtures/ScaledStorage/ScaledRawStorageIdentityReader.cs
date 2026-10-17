using System.Buffers.Binary;
using Microsoft.Extensions.Options;

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

    internal static void Check(long deadlineStart, IOptions<ScaledStorageExecutionOptions> executionOptions, TimeProvider timeProvider, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (timeProvider.GetElapsedTime(deadlineStart) >= executionOptions.Value.PreparationTimeout)
        {
            throw new TimeoutException(DeadlineMessage);
        }
    }
}

internal static class ScaledRawStorageSeedRunner
{

    internal static long Run(ScaledRawStorageCorpus corpus, ScaledRawStorageZoneTreeEngine engine, byte[] scratch, int recordCount, long deadlineStart, ref long attempts, ref long successfulWrites, IOptions<ScaledStorageExecutionOptions> executionOptions, TimeProvider timeProvider, CancellationToken token)
    {
        const int IndexInitialValue = 0;
        const int EmptyIndexCancellationCheckStride = 0;

        var settings = executionOptions.Value;
        settings.Validate();
        var started = timeProvider.GetTimestamp();
        for (var index = IndexInitialValue; index < recordCount; index++)
        {
            if (index % settings.CancellationCheckInterval == EmptyIndexCancellationCheckStride)
            {
                ScaledRawStoragePreparationGuard.Check(deadlineStart, executionOptions, timeProvider, token);
            }

            corpus.WriteValue(index, scratch);
            attempts++;
            engine.Upsert(index, scratch);
            successfulWrites++;
        }

        ScaledRawStoragePreparationGuard.Check(deadlineStart, executionOptions, timeProvider, token);
        return timeProvider.GetTimestamp() - started;
    }
}
