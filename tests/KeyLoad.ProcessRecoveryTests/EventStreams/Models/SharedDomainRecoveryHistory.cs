using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
namespace KeyLoad.ProcessRecoveryTests.EventStreams;

internal sealed record SharedDomainRecoveryHistory(Command<DatabaseCommand> Operation, Result<object> Result, PartitionRef Partition,
    byte[] OutcomeKey, byte[] OutcomeBytes, byte[] LocatorKey, byte[] LocatorBytes);
