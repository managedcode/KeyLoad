using System.Collections.Immutable;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.DatabaseComposition;

internal sealed record CompositionBoundReferenceCommand(CommandRequest Request, Command<DatabaseCommand> Operation,
    CommitReceipt Receipt, bool Derived, ImmutableArray<EntityRef> AuthorityReferences);
