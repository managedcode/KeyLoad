namespace KeyLoad.UnitTests.Messaging;

internal sealed record QueueDeadLetterSelectedRepair(byte[] Key, byte[] Bytes);
