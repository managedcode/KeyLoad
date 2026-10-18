using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed record AtomicTriadOutboxContinuation(CommandRequest Command, Command<DatabaseCommand> Healthy,
    Result<object> Result, Result<object> Purged, string[] Image, long Position);
