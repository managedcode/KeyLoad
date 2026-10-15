namespace KeyLoad.Core.Features.Messaging;

internal sealed record QueueRedriveOrderPlan(string? OrderingKey, long ActiveOrderSequence, long NextOrderSequence);
