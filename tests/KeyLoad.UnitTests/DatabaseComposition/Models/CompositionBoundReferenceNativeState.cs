using System.Collections.Immutable;

namespace KeyLoad.UnitTests.Features.DatabaseComposition;

internal sealed record CompositionBoundReferenceNativeState(EntityRef From, EntityRef To, string CallerPayload,
    ImmutableArray<CompositionBoundReferenceNativeCommand> Commands);
