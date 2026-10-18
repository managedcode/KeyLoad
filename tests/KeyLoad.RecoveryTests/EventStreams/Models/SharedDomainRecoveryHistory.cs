using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
namespace KeyLoad.RecoveryTests.Features.EventStreams;

internal sealed record SharedDomainRecoveryHistory(Command<DatabaseCommand> Operation, Result<object> Result, PartitionRef Partition,
    byte[] OutcomeKey, byte[] OutcomeBytes, byte[] LocatorKey, byte[] LocatorBytes);
