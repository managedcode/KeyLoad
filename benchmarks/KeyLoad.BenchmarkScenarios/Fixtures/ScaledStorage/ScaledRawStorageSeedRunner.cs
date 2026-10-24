using Microsoft.Extensions.Options;

namespace KeyLoad.BenchmarkScenarios.BenchmarkComparisons;

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
