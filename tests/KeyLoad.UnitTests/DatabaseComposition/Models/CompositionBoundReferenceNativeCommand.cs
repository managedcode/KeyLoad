using System.Collections.Immutable;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Features.DatabaseComposition;

internal sealed record CompositionBoundReferenceNativeCommand(CommandRequest Request, Command<DatabaseCommand> Operation,
    CommitReceipt Receipt, bool Derived, ImmutableArray<EntityRef> AuthorityReferences);
