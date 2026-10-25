namespace KeyLoad.ProcessRecoveryTests.Search;

internal sealed record ProjectionCommittedBytes(byte[] Vector, byte[] Lineage, byte[] Effect,
    byte[] Outcome, byte[] OutboxHead, byte[] OutboxEntry);
