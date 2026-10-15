using System.Collections.Immutable;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed record QueueLifecycleCeilingRows(QueueLaneRef Lane, MessageMetadata Metadata,
    QueueCounters Counters, ImmutableArray<KeyValueRecord> OriginalRows, CommandRequest Request, DeliveryCommand? Delivery = null);
