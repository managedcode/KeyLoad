namespace KeyLoad.UnitTests.Messaging;

internal sealed record QueueLifecycleConditionCut(long Position, byte[] Identity, string[] Image);
