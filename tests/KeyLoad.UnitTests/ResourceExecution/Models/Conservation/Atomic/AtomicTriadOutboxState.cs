using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
namespace KeyLoad.UnitTests.ResourceExecution;

internal sealed record AtomicTriadOutboxState(CommandRequest Command, Command<DatabaseCommand> Failed,
    Result<object> Refusal, Command<DatabaseCommand> Seed, Result<object> SeedResult, Command<DatabaseCommand> Purge);
