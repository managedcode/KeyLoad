namespace KeyLoad.FunctionalTests.Messaging;

internal sealed record QueueDeadLetterAdmissionOwnedRow(byte[] Key, byte[] Original);
