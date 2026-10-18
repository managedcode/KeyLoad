using ManagedCode.Communication.Commands;
using System.Collections.Immutable;

namespace KeyLoad.UnitTests.Features.DatabaseComposition;

internal sealed record CompositionBoundReferenceNativeCommand(CommandRequest Request, Command<DatabaseCommand> Operation,
    CommitReceipt Receipt, bool Derived, ImmutableArray<EntityRef> AuthorityReferences);
