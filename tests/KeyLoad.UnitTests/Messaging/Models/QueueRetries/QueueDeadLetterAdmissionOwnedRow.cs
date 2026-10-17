namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed record QueueDeadLetterAdmissionOwnedRow(byte[] Key, byte[] Original);
