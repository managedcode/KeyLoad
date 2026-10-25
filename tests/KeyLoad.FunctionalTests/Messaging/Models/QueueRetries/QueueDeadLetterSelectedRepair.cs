namespace KeyLoad.FunctionalTests.Messaging;

internal sealed record QueueDeadLetterSelectedRepair(byte[] Key, byte[] Bytes);
