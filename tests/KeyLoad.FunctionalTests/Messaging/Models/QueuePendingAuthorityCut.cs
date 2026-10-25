namespace KeyLoad.FunctionalTests.Messaging;

internal sealed record QueuePendingAuthorityCut(long Position, byte[] Identity, string[] Rows);
