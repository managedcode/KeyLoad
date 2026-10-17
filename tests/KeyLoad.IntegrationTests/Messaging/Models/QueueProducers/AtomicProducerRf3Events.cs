using System.Collections.Immutable;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record AtomicProducerRf3Events(StreamHead Head, ImmutableArray<EventRecord> Records);
