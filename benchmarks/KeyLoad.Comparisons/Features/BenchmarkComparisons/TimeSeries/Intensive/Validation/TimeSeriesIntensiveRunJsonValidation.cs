namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimeSeriesIntensiveRunJsonValidation
{
    internal static int Validate(TimeSeriesIntensiveRunResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!Enum.IsDefined(result.Scenario) || result.TimestampFrequency <= TimeSeriesIntensiveRunJsonFields.Zero
            || result.Attempts.Length != TimeSeriesIntensiveRuntimePolicy.TotalAttempts)
        {
            throw new ArgumentException(TimeSeriesIntensiveRunJsonFields.InvalidRun, nameof(result));
        }

        ValidateRunFailure(result.Failure);
        ValidateRepetitions(result);
        var observed = TimeSeriesIntensiveRunJsonFields.Zero;
        var attempts = result.Attempts.Span;
        for (var slot = TimeSeriesIntensiveRunJsonFields.Zero; slot < attempts.Length; slot++)
        {
            var attempt = attempts[slot];
            ValidateSlot(attempt, slot);
            if (attempt.Outcome != TimeSeriesIntensiveOutcome.NotStarted)
            {
                ValidateFailure(attempt.Failure);
                ValidateFailure(attempt.CleanupFailure);
                observed++;
            }
        }

        return observed;
    }

    private static void ValidateSlot(TimeSeriesIntensiveAttempt attempt, int slot)
    {
        var index = slot % TimeSeriesIntensiveProfile.OperationCount;
        if (attempt.Repetition != slot / TimeSeriesIntensiveProfile.OperationCount || attempt.Index != index
            || attempt.Worker != index % TimeSeriesIntensiveProfile.Concurrency || !Enum.IsDefined(attempt.Outcome)
            || (attempt.Outcome != TimeSeriesIntensiveOutcome.NotStarted
                && (attempt.LatencyTicks < TimeSeriesIntensiveRunJsonFields.Zero || attempt.ValidationTicks < TimeSeriesIntensiveRunJsonFields.Zero)))
        {
            throw new ArgumentException(TimeSeriesIntensiveRunJsonFields.InvalidSlot, nameof(attempt));
        }
    }

    private static void ValidateRepetitions(TimeSeriesIntensiveRunResult result)
    {
        if (result.Repetitions.IsDefault)
        {
            throw new ArgumentException(TimeSeriesIntensiveRunJsonFields.InvalidRepetitions, nameof(result));
        }

        foreach (var repetition in result.Repetitions)
        {
            if (repetition is null)
            {
                throw new ArgumentException(TimeSeriesIntensiveRunJsonFields.InvalidRepetitions, nameof(result));
            }

            ValidateRunFailure(repetition.Failure);
            ValidateMeasurement(repetition.Warmup.Measurement);
            ValidateMeasurement(repetition.Measured.Measurement);
        }
    }

    private static void ValidateRunFailure(TimeSeriesIntensiveRunFailure? failure)
    {
        if (failure is not { } original)
        {
            return;
        }

        if (!Enum.IsDefined(original.Stage) || !Enum.IsDefined(original.Outcome))
        {
            throw new ArgumentException(TimeSeriesIntensiveRunJsonFields.InvalidEnum, nameof(failure));
        }

        ValidateFailure(original.Failure);
    }

    private static void ValidateFailure(TimeSeriesIntensiveFailure failure)
    {
        if (!Enum.IsDefined(failure.Origin) || (failure.KeyLoadCode is { } code && !Enum.IsDefined(code)))
        {
            throw new ArgumentException(TimeSeriesIntensiveRunJsonFields.InvalidEnum, nameof(failure));
        }
    }

    private static void ValidateMeasurement(TimeSeriesIntensiveMeasurement? measurement)
    {
        if (measurement is not { } original)
        {
            return;
        }

        if (!double.IsFinite(original.WallSeconds) || !double.IsFinite(original.Throughput) || !double.IsFinite(original.ValidationWorkerSeconds)
            || !double.IsFinite(original.P50Milliseconds) || !double.IsFinite(original.P95Milliseconds) || !double.IsFinite(original.P99Milliseconds))
        {
            throw new ArgumentException(TimeSeriesIntensiveRunJsonFields.InvalidMeasurement, nameof(measurement));
        }
    }
}
