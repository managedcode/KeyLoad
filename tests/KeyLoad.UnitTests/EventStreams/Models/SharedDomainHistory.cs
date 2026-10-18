using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
namespace KeyLoad.UnitTests.Features.EventStreams;

internal sealed record SharedDomainHistory(PartitionRef Partition, Command<DatabaseCommand> Operation, Result<object> Result,
    byte[] OutcomeKey, byte[] OutcomeBytes, byte[] LocatorKey, byte[] LocatorBytes);
