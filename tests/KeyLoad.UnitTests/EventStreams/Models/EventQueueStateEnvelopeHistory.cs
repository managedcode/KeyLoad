using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
namespace KeyLoad.UnitTests.EventStreams;

internal sealed record EventQueueStateEnvelopeHistory(Command<DatabaseCommand> Operation, Result<object> Result,
    byte[] OutcomeBytes, byte[] LocatorBytes, bool ConsumedReceive = false);
