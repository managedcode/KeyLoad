using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
namespace KeyLoad.UnitTests.Features.EventStreams;

internal sealed record EventQueueStateEnvelopeHistory(Command<DatabaseCommand> Operation, Result<object> Result,
    byte[] OutcomeBytes, byte[] LocatorBytes, bool ConsumedReceive = false);
