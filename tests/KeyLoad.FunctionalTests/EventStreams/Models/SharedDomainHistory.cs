using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
namespace KeyLoad.FunctionalTests.EventStreams;

internal sealed record SharedDomainHistory(PartitionRef Partition, Command<DatabaseCommand> Operation, Result<object> Result,
    byte[] OutcomeKey, byte[] OutcomeBytes, byte[] LocatorKey, byte[] LocatorBytes);
