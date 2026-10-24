namespace KeyLoad.Core.Messaging;

internal sealed record QueueRedriveOrderPlan(string? OrderingKey, long ActiveOrderSequence, long NextOrderSequence);
