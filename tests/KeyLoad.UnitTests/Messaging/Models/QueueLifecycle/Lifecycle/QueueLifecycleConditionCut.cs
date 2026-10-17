namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed record QueueLifecycleConditionCut(long Position, byte[] Identity, string[] Image);
