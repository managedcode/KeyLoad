namespace KeyLoad.UnitTests.Messaging;

internal sealed record QueuePendingAuthorityCut(long Position, byte[] Identity, string[] Rows);
