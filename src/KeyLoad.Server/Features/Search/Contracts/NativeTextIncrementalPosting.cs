namespace KeyLoad.Server.Features.Search;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(NativeTextIncrementalAliases.Posting)]
internal sealed record NativeTextIncrementalPosting(
    [property: global::Orleans.Id(0)] ulong Token,
    [property: global::Orleans.Id(1)] ulong Record,
    [property: global::Orleans.Id(2)] ulong PreviousToken);
