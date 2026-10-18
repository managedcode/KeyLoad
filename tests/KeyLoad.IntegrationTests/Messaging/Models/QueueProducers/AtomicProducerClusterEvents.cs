using System.Collections.Immutable;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record AtomicProducerClusterEvents(StreamHead Head, ImmutableArray<EventRecord> Records);
