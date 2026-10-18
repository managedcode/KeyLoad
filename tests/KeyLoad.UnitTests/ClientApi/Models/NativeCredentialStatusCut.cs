namespace KeyLoad.UnitTests.Features.ClientApi;

internal sealed record NativeCredentialStatusCut(IReadOnlyDictionary<string, string> RowHashes, long Position,
    (long AppliedPosition, long StorePosition) Cut, DateTimeOffset Clock);
