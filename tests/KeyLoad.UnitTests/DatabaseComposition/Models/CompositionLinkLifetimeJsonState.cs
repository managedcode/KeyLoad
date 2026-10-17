namespace KeyLoad.UnitTests.Features.DatabaseComposition;

internal sealed record CompositionLinkLifetimeJsonState(EntityRef From, EntityRef To, ReplicatedOperation Seed);
