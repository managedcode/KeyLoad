using System.Globalization;
using System.Text;
using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons.Targets;

internal sealed class KeyLoadOutboxDiagnosticLine(IOptions<NativeComparisonDiagnosticOptions> options)
{
    private readonly NativeComparisonDiagnosticOptions settings = NativeComparisonDiagnosticOptions.Require(options).Value;
    private const int NoObservedItems = 0;
    private const int SingleItemCount = 1;
    private const int AdjacentElementOffset = 1;
    private const char SpaceSeparator = ' ';
    private const char EmptyIdentityMarker = '~';

    private const string Prefix = "KeyLoadOutboxDiagnostic ";
    private const string InvalidStatus = "The outbox diagnostic status is invalid.";
    private const string InvalidConsumer = "The outbox diagnostic consumer is invalid.";
    private const string InvalidCase = "The outbox diagnostic case is invalid.";
    private const string InvalidOutput = "The outbox diagnostic line exceeds its output bounds.";
    private const string FormatTemplate = "{0}scenario={1} repetition={2} tail={3} firstAvailable={4} "
        + "storedRecords={5} storedBytes={6} activeConsumerCount={7} minimumActiveCheckpoint={8}";
    private static readonly CompositeFormat ParsedFormat = CompositeFormat.Parse(FormatTemplate);
    internal const string UnavailableLine = Prefix + "observation=unavailable";

    internal string Format(ComparisonCase failed, OutboxStatus status)
    {
        const int FirstElementIndex = 0;
        const int MissingItemIndex = -1;
        const int NoObservedItems = 0;

        ArgumentNullException.ThrowIfNull(failed);
        ArgumentNullException.ThrowIfNull(status);
        ValidateCase(failed);
        if (status.Head is null || status.Consumers.IsDefault || status.Consumers.Length > settings.KeyLoadOutboxMaximumConsumers || !ValidHead(status.Head))
        {
            throw new ArgumentException(InvalidStatus, nameof(status));
        }

        var activeCount = FirstElementIndex;
        long minimumCheckpoint = MissingItemIndex;
        foreach (var consumer in status.Consumers)
        {
            if (consumer is null || consumer.Checkpoint < NoObservedItems || consumer.Checkpoint > status.Head.Tail)
            {
                throw new ArgumentException(InvalidConsumer, nameof(status));
            }
            if (!consumer.Released)
            {
                minimumCheckpoint = activeCount == NoObservedItems ? consumer.Checkpoint : Math.Min(minimumCheckpoint, consumer.Checkpoint);
                activeCount++;
            }
        }
        return Bound(string.Format(CultureInfo.InvariantCulture, ParsedFormat, Prefix, failed.Scenario,
            failed.Repetition, status.Head.Tail, status.Head.FirstAvailable, status.Head.StoredRecords,
            status.Head.StoredBytes, activeCount, minimumCheckpoint));
    }

    private static void ValidateCase(ComparisonCase failed)
    {
        const int NoObservedItems = 0;

        if (!Enum.IsDefined(failed.Scenario) || failed.Repetition < NoObservedItems)
        {
            throw new ArgumentException(InvalidCase, nameof(failed));
        }
    }

    private static bool ValidHead(OutboxHead head)
        => head.Tail >= NoObservedItems && head.FirstAvailable >= SingleItemCount && head.FirstAvailable - AdjacentElementOffset <= head.Tail
            && head.StoredRecords == head.Tail - (head.FirstAvailable - AdjacentElementOffset) && head.StoredBytes >= NoObservedItems;

    private string Bound(string line)
        => line.All(character => character is >= SpaceSeparator and <= EmptyIdentityMarker)
            && Encoding.ASCII.GetByteCount(line) <= settings.KeyLoadOutboxMaximumBytes
            ? line
            : throw new ArgumentException(InvalidOutput, nameof(line));
}
