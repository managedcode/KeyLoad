using System.Collections.Immutable;
using KeyLoad.Storage;

namespace KeyLoad.FunctionalTests.Messaging;

internal sealed record QueueLifecycleCeilingRows(QueueLaneRef Lane, MessageMetadata Metadata,
    QueueCounters Counters, ImmutableArray<KeyValueRecord> OriginalRows, CommandRequest Request, DeliveryCommand? Delivery = null);
