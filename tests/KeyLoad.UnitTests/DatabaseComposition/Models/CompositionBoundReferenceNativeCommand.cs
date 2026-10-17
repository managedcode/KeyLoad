using System.Collections.Immutable;

namespace KeyLoad.UnitTests.Features.DatabaseComposition;

internal sealed record CompositionBoundReferenceNativeCommand(CommandRequest Request, ReplicatedOperation Operation,
    CommitReceipt Receipt, bool Derived, ImmutableArray<EntityRef> AuthorityReferences);
