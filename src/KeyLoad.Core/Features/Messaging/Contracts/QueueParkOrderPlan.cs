namespace KeyLoad.Core.Features.Messaging;

internal sealed record QueueParkOrderPlan(byte[]? KeyToDelete, long ActiveOrderSequence);
