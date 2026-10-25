namespace KeyLoad.FunctionalTests.Messaging;

internal sealed record QueueLifecycleConditionCut(long Position, byte[] Identity, string[] Image);
