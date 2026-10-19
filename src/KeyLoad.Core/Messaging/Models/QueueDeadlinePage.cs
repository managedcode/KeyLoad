using System.Collections.Immutable;

namespace KeyLoad.Core.Features.Messaging;

/// <summary>Reports one bounded execution queue-deadline read and its real cursor.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(QueueDeadlinePage.SerializerAlias)]
public sealed record QueueDeadlinePage([property: Orleans.Id(0)] ImmutableArray<QueueDeadlineHint> Jobs,
    [property: Orleans.Id(1)] ImmutableArray<ErrorCode> Rejected,
    [property: Orleans.Id(2)] QueueDeadlineCursor Cursor,
    [property: Orleans.Id(3)] int ExaminedRecords,
    [property: Orleans.Id(4)] long ExaminedBytes)
{
    internal const string SerializerAlias = "keyload.core.queue-deadline-page";
}
