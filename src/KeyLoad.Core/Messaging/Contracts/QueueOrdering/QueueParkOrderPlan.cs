namespace KeyLoad.Core.Messaging;

internal sealed record QueueParkOrderPlan(byte[]? KeyToDelete, long ActiveOrderSequence);
