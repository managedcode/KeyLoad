using System.Collections.Immutable;

namespace KeyLoad.ClusterTests.Messaging;

internal sealed record AtomicProducerClusterEvents(StreamHead Head, ImmutableArray<EventRecord> Records);
