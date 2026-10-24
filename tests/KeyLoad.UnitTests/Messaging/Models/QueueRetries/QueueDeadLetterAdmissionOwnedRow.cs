namespace KeyLoad.UnitTests.Messaging;

internal sealed record QueueDeadLetterAdmissionOwnedRow(byte[] Key, byte[] Original);
