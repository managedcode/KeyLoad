namespace KeyLoad.UnitTests.ClientApi;

internal sealed record CredentialStatusCut(IReadOnlyDictionary<string, string> RowHashes,
    KeyValuePair<byte[], byte[]>[] Rows, long Position, (long AppliedPosition, long StorePosition) Cut,
    DateTimeOffset Clock, byte[] Identity);
