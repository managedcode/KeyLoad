namespace KeyLoad.UnitTests.Features.Messaging;

internal enum QueueLifecycleConditionFault { MetadataIdentity, BodyIdentity }
internal enum QueueLifecycleConditionRefusal { Version, Generation, Missing, Cancelled }
internal sealed record QueueLifecycleConditionCut(long Position, byte[] Identity, string[] Image);
