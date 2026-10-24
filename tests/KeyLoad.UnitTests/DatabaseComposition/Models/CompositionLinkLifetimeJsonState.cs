using ManagedCode.Communication.Commands;
namespace KeyLoad.UnitTests.DatabaseComposition;

internal sealed record CompositionLinkLifetimeJsonState(EntityRef From, EntityRef To, Command<DatabaseCommand> Seed);
