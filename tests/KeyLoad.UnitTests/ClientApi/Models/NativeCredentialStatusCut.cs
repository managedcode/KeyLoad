namespace KeyLoad.UnitTests.Features.ClientApi;

internal sealed record NativeCredentialStatusCut(IReadOnlyDictionary<string, string> RowHashes,
    KeyValuePair<byte[], byte[]>[] Rows, long Position, (long AppliedPosition, long StorePosition) Cut,
    DateTimeOffset Clock, byte[] Identity);
