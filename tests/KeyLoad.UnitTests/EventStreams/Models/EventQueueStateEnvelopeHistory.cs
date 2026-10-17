namespace KeyLoad.UnitTests.Features.EventStreams;

internal sealed record EventQueueStateEnvelopeHistory(ReplicatedOperation Operation, OperationResult Result,
    byte[] OutcomeBytes, byte[] LocatorBytes, bool ConsumedReceive = false);
