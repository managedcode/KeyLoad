namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed record RemoteTransferCapacityAuthorityDamage(byte[] CounterKey,
    IReadOnlyList<KeyValuePair<byte[], byte[]>> OriginalRows);
