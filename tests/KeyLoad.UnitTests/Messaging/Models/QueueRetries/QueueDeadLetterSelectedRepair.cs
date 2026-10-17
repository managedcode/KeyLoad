namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed record QueueDeadLetterSelectedRepair(byte[] Key, byte[] Bytes);
