using System.Collections.Immutable;

namespace KeyLoad.IntegrationTests.Messaging;

internal sealed record AtomicProducerClusterEvents(StreamHead Head, ImmutableArray<EventRecord> Records);
