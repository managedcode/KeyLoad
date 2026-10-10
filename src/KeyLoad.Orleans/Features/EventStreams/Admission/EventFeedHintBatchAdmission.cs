using Microsoft.Extensions.Options;
using global::Orleans.Providers;
using global::Orleans.Serialization;

namespace KeyLoad.Orleans;

internal sealed class EventFeedHintBatchAdmission(Serializer<MemoryMessageBody> nativeSerializer,
    IOptions<DatabaseLimits> databaseOptions, IOptions<GrainRoutingOptions> routingOptions)
{
    private const int Version = 1;
    private const long FirstRevision = 1;
    private const string Invalid = "The native event feed advisory batch is invalid.";

    internal EventFeedWakeupHint Require<T>(IEnumerable<T> originalEvents,
        Dictionary<string, object>? originalContext)
    {
        ArgumentNullException.ThrowIfNull(originalEvents);
        if (originalContext is not null)
        { throw Errors.Fail(ErrorCode.Validation, Invalid); }
        using var iterator = originalEvents.GetEnumerator();
        if (!iterator.MoveNext() || iterator.Current is not EventFeedWakeupHint hint
            || iterator.MoveNext() || hint.Version != Version || hint.MapId == Guid.Empty
            || hint.MapRevision < FirstRevision || hint.CoverageGeneration < FirstRevision)
        { throw Errors.Fail(ErrorCode.Validation, Invalid); }
        var limits = databaseOptions.Value;
        limits.Validate();
        var maximumBytes = checked((int)Math.Min(limits.MaxBatchBytes, limits.MaxQueryReadBytes));
        using var counter = new GrainNativeCountingWriter(maximumBytes, routingOptions, CancellationToken.None);
        nativeSerializer.Serialize(new MemoryMessageBody(new object[] { hint }, null), counter);
        return hint;
    }
}
