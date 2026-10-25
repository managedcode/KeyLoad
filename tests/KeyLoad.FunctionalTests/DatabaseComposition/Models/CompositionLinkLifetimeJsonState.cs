using ManagedCode.Communication.Commands;
namespace KeyLoad.FunctionalTests.DatabaseComposition;

internal sealed record CompositionLinkLifetimeJsonState(EntityRef From, EntityRef To, Command<DatabaseCommand> Seed);
