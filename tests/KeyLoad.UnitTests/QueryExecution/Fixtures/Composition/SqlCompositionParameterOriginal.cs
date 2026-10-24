using ManagedCode.Communication.Commands;
namespace KeyLoad.UnitTests.QueryExecution;

internal sealed record SqlCompositionParameterOriginal(CommandRequest Command, Command<DatabaseCommand> Operation,
    CommitReceipt Expected, Mutation Effect);
