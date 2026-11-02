
namespace KeyLoad.Clustering;

internal static class EventFeedHintBatchAdmission
{
    private const int Version = 1;
    private const long FirstRevision = 1;
    private const string Invalid = "The execution event feed advisory batch is invalid.";

    internal static EventFeedWakeupHint Require<T>(IEnumerable<T> originalEvents,
        Dictionary<string, object>? /* KLD0041: The Orleans queue adapter contract supplies a heterogeneous request-context map; this advisory admission explicitly rejects any supplied map. */ originalContext)
    {
        ArgumentNullException.ThrowIfNull(originalEvents) /* KLD0041: BCL null validation accepts the originalEvents IEnumerable reference through its object parameter. */;
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
