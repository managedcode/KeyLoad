using System.Collections.Immutable;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Messaging;

internal sealed record QueueLifecycleCeilingRows(QueueLaneRef Lane, MessageMetadata Metadata,
    QueueCounters Counters, ImmutableArray<KeyValueRecord> OriginalRows, CommandRequest Request, DeliveryCommand? Delivery = null);
