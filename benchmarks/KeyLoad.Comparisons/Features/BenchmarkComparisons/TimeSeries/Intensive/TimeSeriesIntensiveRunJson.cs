using System.Text.Json;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimeSeriesIntensiveRunJson
{
    internal static void Write(Utf8JsonWriter writer, TimeSeriesIntensiveRunResult result)
    {
        ArgumentNullException.ThrowIfNull(writer);
        var observed = TimeSeriesIntensiveRunJsonValidation.Validate(result);
        writer.WriteStartObject();
        writer.WriteNumber(TimeSeriesIntensiveRunJsonFields.SchemaVersion, TimeSeriesIntensiveRunJsonFields.SchemaVersionValue);
        writer.WriteString(TimeSeriesIntensiveRunJsonFields.Scenario, result.Scenario.ToString());
        writer.WriteNumber(TimeSeriesIntensiveRunJsonFields.TimestampFrequency, result.TimestampFrequency);
        writer.WriteBoolean(TimeSeriesIntensiveRunJsonFields.SeedVerified, result.SeedVerified);
        writer.WriteBoolean(TimeSeriesIntensiveRunJsonFields.WorkloadSucceeded, result.Succeeded);
        writer.WriteNumber(TimeSeriesIntensiveRunJsonFields.PlannedSlotCount, result.Attempts.Length);
        writer.WriteNumber(TimeSeriesIntensiveRunJsonFields.ObservedAttemptCount, observed);
        writer.WriteNumber(TimeSeriesIntensiveRunJsonFields.UnstartedSlotCount, result.Attempts.Length - observed);
        WriteRunFailure(writer, result.Failure);
        WriteRepetitions(writer, result);
        TimeSeriesIntensiveRunJsonAttempts.Write(writer, result.Attempts.Span);
        writer.WriteEndObject();
    }

    private static void WriteRepetitions(Utf8JsonWriter writer, TimeSeriesIntensiveRunResult result)
    {
        writer.WriteStartArray(TimeSeriesIntensiveRunJsonFields.Repetitions);
        foreach (var repetition in result.Repetitions)
        {
            writer.WriteStartObject();
            writer.WriteNumber(TimeSeriesIntensiveRunJsonFields.Repetition, repetition.Repetition);
            WritePhase(writer, TimeSeriesIntensiveRunJsonFields.Warmup, repetition.Warmup);
            WritePhase(writer, TimeSeriesIntensiveRunJsonFields.Measured, repetition.Measured);
            writer.WriteBoolean(TimeSeriesIntensiveRunJsonFields.FinalVerified, repetition.FinalVerified);
            WriteRunFailure(writer, repetition.Failure);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteRunFailure(Utf8JsonWriter writer, TimeSeriesIntensiveRunFailure? failure)
    {
        if (failure is not { } original)
        {
            writer.WriteNull(TimeSeriesIntensiveRunJsonFields.Failure);
            return;
        }

        writer.WriteStartObject(TimeSeriesIntensiveRunJsonFields.Failure);
        writer.WriteString(TimeSeriesIntensiveRunJsonFields.Stage, original.Stage.ToString());
        writer.WriteNumber(TimeSeriesIntensiveRunJsonFields.Repetition, original.Repetition);
        writer.WriteString(TimeSeriesIntensiveRunJsonFields.Outcome, original.Outcome.ToString());
        TimeSeriesIntensiveRunJsonAttempts.WriteFailure(writer, TimeSeriesIntensiveRunJsonFields.Failure, original.Failure);
        writer.WriteEndObject();
    }

    private static void WritePhase(Utf8JsonWriter writer, string field, TimeSeriesIntensivePhaseResult phase)
    {
        writer.WriteStartObject(field);
        writer.WriteBoolean(TimeSeriesIntensiveRunJsonFields.Complete, phase.Complete);
        writer.WriteBoolean(TimeSeriesIntensiveRunJsonFields.Succeeded, phase.Succeeded);
        writer.WriteNumber(TimeSeriesIntensiveRunJsonFields.WallTicks, phase.WallTicks);
        writer.WriteNumber(TimeSeriesIntensiveRunJsonFields.WorkersStarted, phase.WorkersStarted);
        writer.WriteNumber(TimeSeriesIntensiveRunJsonFields.PeakClientCalls, phase.PeakClientCalls);
        writer.WriteNumber(TimeSeriesIntensiveRunJsonFields.PeakDecodedResponses, phase.PeakDecodedResponses);
        WriteMeasurement(writer, phase.Measurement);
        writer.WriteEndObject();
    }

    private static void WriteMeasurement(Utf8JsonWriter writer, TimeSeriesIntensiveMeasurement? measurement)
    {
        if (measurement is not { } original)
        {
            writer.WriteNull(TimeSeriesIntensiveRunJsonFields.Measurement);
            return;
        }

        writer.WriteStartObject(TimeSeriesIntensiveRunJsonFields.Measurement);
        writer.WriteNumber(TimeSeriesIntensiveRunJsonFields.Attempted, original.Attempted);
        writer.WriteNumber(TimeSeriesIntensiveRunJsonFields.Succeeded, original.Succeeded);
        writer.WriteNumber(TimeSeriesIntensiveRunJsonFields.WallSeconds, original.WallSeconds);
        writer.WriteNumber(TimeSeriesIntensiveRunJsonFields.Throughput, original.Throughput);
        writer.WriteNumber(TimeSeriesIntensiveRunJsonFields.ValidationWorkerSeconds, original.ValidationWorkerSeconds);
        writer.WriteNumber(TimeSeriesIntensiveRunJsonFields.P50Milliseconds, original.P50Milliseconds);
        writer.WriteNumber(TimeSeriesIntensiveRunJsonFields.P95Milliseconds, original.P95Milliseconds);
        writer.WriteNumber(TimeSeriesIntensiveRunJsonFields.P99Milliseconds, original.P99Milliseconds);
        writer.WriteEndObject();
    }
}
