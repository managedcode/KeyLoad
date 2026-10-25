using ManagedCode.Communication.Commands;
namespace KeyLoad.FunctionalTests.QueryExecution;

internal sealed record SqlCompositionParameterOriginal(CommandRequest Command, Command<DatabaseCommand> Operation,
    CommitReceipt Expected, Mutation Effect);
