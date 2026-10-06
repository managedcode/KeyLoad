using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

internal static class ScaledRawStorageVerification
{
    private const string MissingMessage = "A seeded scaled record is missing from the native engine.";
    private const string PresentMessage = "The reserved scaled miss index is present in the native engine.";
    private const string MismatchMessage = "The native value differs from the complete deterministic value.";
    private const string DeadlineMessage = "The scaled fixture preparation deadline expired.";

    internal static ScaledRawStorageVerificationResult Run(ScaledRawStorageCorpus corpus,
        ScaledRawStorageZoneTreeEngine engine, byte[] expectedScratch, int recordCount, ref long readCalls,
        long deadlineStart, bool enforceBudget, IOptions<ScaledStorageExecutionOptions> executionOptions, CancellationToken token)
    {
        const long VerifiedInitialValue = 0L;
        const int IndexInitialValue = 0;

        var settings = executionOptions.Value;
        settings.Validate();
        var started = Stopwatch.GetTimestamp();
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var verified = VerifiedInitialValue;
        for (var index = IndexInitialValue; index < recordCount; index++)
        {
            CheckBoundary(index, deadlineStart, enforceBudget, settings.PreparationTimeout, settings.CancellationCheckInterval, token);
            readCalls++;
            if (!engine.TryRead(index, out var actual))
            {
                throw new InvalidOperationException(MissingMessage);
            }

            corpus.WriteValue(index, expectedScratch);
            if (!actual.Span.SequenceEqual(expectedScratch))
            {
                throw new InvalidOperationException(MismatchMessage);
            }

            digest.AppendData(actual.Span);
            verified++;
        }

        CheckBoundary(recordCount, deadlineStart, enforceBudget, settings.PreparationTimeout, settings.CancellationCheckInterval, token);
        readCalls++;
        if (engine.TryRead(recordCount, out _))
        {
            throw new InvalidOperationException(PresentMessage);
        }

        CheckCompletion(deadlineStart, enforceBudget, settings.PreparationTimeout, token);
        return new ScaledRawStorageVerificationResult(
            verified,
            Convert.ToHexStringLower(digest.GetHashAndReset()),
            Stopwatch.GetTimestamp() - started);
    }

    private static void CheckBoundary(int operation, long deadlineStart, bool enforceBudget,
        TimeSpan preparationTimeout, int cancellationCheckInterval, CancellationToken token)
    {
        const int EmptyOperationCancellationCheckStride = 0;

        if (!enforceBudget || operation % cancellationCheckInterval != EmptyOperationCancellationCheckStride)
        {
            return;
        }

        token.ThrowIfCancellationRequested();
        if (Stopwatch.GetElapsedTime(deadlineStart) >= preparationTimeout)
        {
            throw new TimeoutException(DeadlineMessage);
        }
    }

    private static void CheckCompletion(long deadlineStart, bool enforceBudget, TimeSpan preparationTimeout, CancellationToken token)
    {
        if (!enforceBudget)
        {
            return;
        }

        token.ThrowIfCancellationRequested();
        if (Stopwatch.GetElapsedTime(deadlineStart) >= preparationTimeout)
        {
            throw new TimeoutException(DeadlineMessage);
        }
    }
}

internal readonly record struct ScaledRawStorageVerificationResult(long VerifiedRecords, string Digest, long ElapsedTicks);
