namespace KeyLoad.FunctionalTests.Messaging;

internal sealed record RemoteTransferCapacityAuthorityDamage(byte[] CounterKey,
    IReadOnlyList<KeyValuePair<byte[], byte[]>> OriginalRows);
