using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed record AtomicTriadOutboxContinuation(CommandRequest Command, Command<DatabaseCommand> Healthy,
    Result<object> Result, Result<object> Purged, string[] Image, long Position);
