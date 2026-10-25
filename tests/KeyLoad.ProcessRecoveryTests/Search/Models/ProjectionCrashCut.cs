namespace KeyLoad.ProcessRecoveryTests.Search;

internal sealed record ProjectionCrashCut(byte[]? Vector, byte[]? Lineage, byte[]? Effect,
    byte[]? Outcome, byte[]? OutboxHead, byte[]? OutboxEntry);
