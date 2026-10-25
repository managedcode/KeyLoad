using System.Collections.Immutable;

namespace KeyLoad.FunctionalTests.DatabaseComposition;

internal sealed record CompositionBoundReferenceState(EntityRef From, EntityRef To, string CallerPayload,
    ImmutableArray<CompositionBoundReferenceCommand> Commands);
