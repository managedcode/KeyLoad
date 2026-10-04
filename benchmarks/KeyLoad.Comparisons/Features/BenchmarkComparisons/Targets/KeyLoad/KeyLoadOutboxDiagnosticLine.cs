using System.Globalization;
using System.Text;

namespace KeyLoad.Comparisons.Targets;

internal static class KeyLoadOutboxDiagnosticLine
{
    private const int MaximumBytes = 512;
    private const int MaximumConsumers = 64;
    private const string Prefix = "KeyLoadOutboxDiagnostic ";
    private const string InvalidStatus = "The outbox diagnostic status is invalid.";
    private const string InvalidConsumer = "The outbox diagnostic consumer is invalid.";
    private const string InvalidCase = "The outbox diagnostic case is invalid.";
    private const string InvalidOutput = "The outbox diagnostic line exceeds its output bounds.";
    private const string FormatTemplate = "{0}scenario={1} repetition={2} tail={3} firstAvailable={4} "
        + "storedRecords={5} storedBytes={6} activeConsumerCount={7} minimumActiveCheckpoint={8}";
    private static readonly CompositeFormat ParsedFormat = CompositeFormat.Parse(FormatTemplate);
    internal const string UnavailableLine = Prefix + "observation=unavailable";

    internal static string Format(ComparisonCase failed, OutboxStatus status)
    {
        ArgumentNullException.ThrowIfNull(failed);
        ArgumentNullException.ThrowIfNull(status);
        ValidateCase(failed);
        if (status.Head is null || status.Consumers.IsDefault || status.Consumers.Length > MaximumConsumers || !ValidHead(status.Head))
        {
            throw new ArgumentException(InvalidStatus, nameof(status));
        }

        var activeCount = 0;
        long minimumCheckpoint = -1;
        foreach (var consumer in status.Consumers)
        {
            if (consumer is null || consumer.Checkpoint < 0 || consumer.Checkpoint > status.Head.Tail)
            {
                throw new ArgumentException(InvalidConsumer, nameof(status));
            }
            if (!consumer.Released)
            {
                minimumCheckpoint = activeCount == 0 ? consumer.Checkpoint : Math.Min(minimumCheckpoint, consumer.Checkpoint);
                activeCount++;
            }
        }
        return Bound(string.Format(CultureInfo.InvariantCulture, ParsedFormat, Prefix, failed.Scenario,
            failed.Repetition, status.Head.Tail, status.Head.FirstAvailable, status.Head.StoredRecords,
            status.Head.StoredBytes, activeCount, minimumCheckpoint));
    }

    private static void ValidateCase(ComparisonCase failed)
    {
        if (!Enum.IsDefined(failed.Scenario) || failed.Repetition < 0)
        {
            throw new ArgumentException(InvalidCase, nameof(failed));
        }
    }

    private static bool ValidHead(OutboxHead head)
        => head.Tail >= 0 && head.FirstAvailable >= 1 && head.FirstAvailable - 1 <= head.Tail
            && head.StoredRecords == head.Tail - (head.FirstAvailable - 1) && head.StoredBytes >= 0;

    private static string Bound(string line)
        => line.All(character => character is >= ' ' and <= '~')
            && Encoding.ASCII.GetByteCount(line) <= MaximumBytes
            ? line
            : throw new ArgumentException(InvalidOutput, nameof(line));
}
