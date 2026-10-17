namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed record QueuePendingAuthorityCut(long Position, byte[] Identity, string[] Rows);
