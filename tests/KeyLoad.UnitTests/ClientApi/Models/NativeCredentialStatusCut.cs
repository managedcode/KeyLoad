namespace KeyLoad.UnitTests.Features.ClientApi;

internal sealed record NativeCredentialStatusCut(IReadOnlyDictionary<string, string> RowHashes, long Position,
    (long LastIndex, long CommittedIndex, long Term) Journal, DateTimeOffset Clock);
