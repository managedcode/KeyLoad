using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed record AtomicTriadOutboxState(CommandRequest Command, Command<DatabaseCommand> Failed,
    Result<object> Refusal, Command<DatabaseCommand> Seed, Result<object> SeedResult, Command<DatabaseCommand> Purge);
