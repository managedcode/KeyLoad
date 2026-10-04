using System.Text.Json;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimeSeriesIntensiveRunJsonAttempts
{
    internal static void Write(Utf8JsonWriter writer, ReadOnlySpan<TimeSeriesIntensiveAttempt> attempts)
    {
        writer.WriteStartArray(TimeSeriesIntensiveRunJsonFields.Attempts);
        for (var slot = TimeSeriesIntensiveRunJsonFields.Zero; slot < attempts.Length; slot++)
        {
            WriteAttempt(writer, slot, attempts[slot]);
        }

        writer.WriteEndArray();
    }

    internal static void WriteFailure(Utf8JsonWriter writer, string field, TimeSeriesIntensiveFailure failure)
    {
        writer.WriteStartObject(field);
        writer.WriteString(TimeSeriesIntensiveRunJsonFields.Origin, failure.Origin.ToString());
        writer.WriteString(TimeSeriesIntensiveRunJsonFields.KeyLoadCode, failure.KeyLoadCode?.ToString());
        if (failure.HttpStatus is { } status)
        {
            writer.WriteNumber(TimeSeriesIntensiveRunJsonFields.HttpStatus, status);
        }
        else
        {
            writer.WriteNull(TimeSeriesIntensiveRunJsonFields.HttpStatus);
        }

        if (failure.SqlState is { } sqlState)
        {
            writer.WriteNumber(TimeSeriesIntensiveRunJsonFields.SqlState, sqlState);
        }
        else
        {
            writer.WriteNull(TimeSeriesIntensiveRunJsonFields.SqlState);
        }

        writer.WriteEndObject();
    }

    private static void WriteAttempt(Utf8JsonWriter writer, int slot, TimeSeriesIntensiveAttempt attempt)
    {
        writer.WriteStartObject();
        writer.WriteNumber(TimeSeriesIntensiveRunJsonFields.Slot, slot);
        writer.WriteNumber(TimeSeriesIntensiveRunJsonFields.Repetition, attempt.Repetition);
        writer.WriteNumber(TimeSeriesIntensiveRunJsonFields.Index, attempt.Index);
        writer.WriteNumber(TimeSeriesIntensiveRunJsonFields.Worker, attempt.Worker);
        writer.WriteBoolean(TimeSeriesIntensiveRunJsonFields.Observed, attempt.Outcome != TimeSeriesIntensiveOutcome.NotStarted);
        if (attempt.Outcome != TimeSeriesIntensiveOutcome.NotStarted)
        {
            WriteObserved(writer, attempt);
        }

        writer.WriteEndObject();
    }

    private static void WriteObserved(Utf8JsonWriter writer, TimeSeriesIntensiveAttempt attempt)
    {
        writer.WriteString(TimeSeriesIntensiveRunJsonFields.Outcome, attempt.Outcome.ToString());
        writer.WriteNumber(TimeSeriesIntensiveRunJsonFields.LatencyTicks, attempt.LatencyTicks);
        writer.WriteNumber(TimeSeriesIntensiveRunJsonFields.ValidationTicks, attempt.ValidationTicks);
        if (attempt.ResultCount is { } count)
        {
            writer.WriteNumber(TimeSeriesIntensiveRunJsonFields.ResultCount, count);
        }
        else
        {
            writer.WriteNull(TimeSeriesIntensiveRunJsonFields.ResultCount);
        }

        writer.WriteString(TimeSeriesIntensiveRunJsonFields.Digest, attempt.Digest.ToHex());
        writer.WriteNumber(TimeSeriesIntensiveRunJsonFields.ReceiptSequence, attempt.ReceiptSequence);
        WriteAcknowledgement(writer, attempt.Acknowledgement);
        WriteFailure(writer, TimeSeriesIntensiveRunJsonFields.Failure, attempt.Failure);
        WriteFailure(writer, TimeSeriesIntensiveRunJsonFields.CleanupFailure, attempt.CleanupFailure);
    }

    private static void WriteAcknowledgement(Utf8JsonWriter writer, TimeSeriesIntensiveAcknowledgement? acknowledgement)
    {
        if (acknowledgement is not { } original)
        {
            writer.WriteNull(TimeSeriesIntensiveRunJsonFields.Acknowledgement);
            return;
        }

        writer.WriteStartObject(TimeSeriesIntensiveRunJsonFields.Acknowledgement);
        writer.WriteString(TimeSeriesIntensiveRunJsonFields.CommandId, original.CommandId);
        writer.WriteNumber(TimeSeriesIntensiveRunJsonFields.Sequence, original.Sequence);
        writer.WriteEndObject();
    }
}
