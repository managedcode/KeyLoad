
namespace KeyLoad.Clustering;

internal static class EventFeedHintBatchAdmission
{
    private const int Version = 1;
    private const long FirstRevision = 1;
    private const string Invalid = "The execution event feed advisory batch is invalid.";

    internal static EventFeedWakeupHint Require<T>(IEnumerable<T> originalEvents,
        Dictionary<string, object>? originalContext)
    {
        ArgumentNullException.ThrowIfNull(originalEvents);
        if (originalContext is not null)
        {
            throw Errors.Fail(ErrorCode.Validation, Invalid);
        }
        using var iterator = originalEvents.GetEnumerator();
        if (!iterator.MoveNext() || iterator.Current is not EventFeedWakeupHint hint
            || iterator.MoveNext() || hint.Version != Version || hint.MapId == Guid.Empty
            || hint.MapRevision < FirstRevision || hint.CoverageGeneration < FirstRevision)
        {
            throw Errors.Fail(ErrorCode.Validation, Invalid);
        }
        return hint;
    }
}
