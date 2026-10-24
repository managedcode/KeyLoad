using KeyLoad.Core;
using KeyLoad.Core.Messaging;

namespace KeyLoad.Clustering;

/// <summary>Reports a real deadline page or a rejected prefix without fabricating read counters.</summary>
[GenerateSerializer, Alias(QueueDeadlineDiscoveryResult.SerializerAlias)]
public sealed record QueueDeadlineDiscoveryResult([property: Id(0)] QueueDeadlinePage? Page,
    [property: Id(1)] QueueDeadlineCursor Cursor, [property: Id(2)] ErrorCode? PrefixError)
{
    internal const string SerializerAlias = "keyload.orleans.queue-deadline-discovery-result";
}
