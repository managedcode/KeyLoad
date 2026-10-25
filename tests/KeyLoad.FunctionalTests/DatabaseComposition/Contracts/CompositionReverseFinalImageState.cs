using ManagedCode.Communication.Commands;
namespace KeyLoad.FunctionalTests.DatabaseComposition;

internal sealed record CompositionReverseFinalImageState(EntityRef Bound, CommandRequest Request,
    Command<DatabaseCommand> Original, CommitReceipt Receipt, Command<DatabaseCommand> Seed, CommitReceipt SeedReceipt);
