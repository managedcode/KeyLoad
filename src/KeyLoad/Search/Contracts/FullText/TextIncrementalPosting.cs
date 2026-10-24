namespace KeyLoad.Server.Search;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(TextIncrementalAliases.Posting)]
internal sealed record TextIncrementalPosting(
    [property: global::Orleans.Id(0)] ulong Token,
    [property: global::Orleans.Id(1)] ulong Record,
    [property: global::Orleans.Id(2)] ulong PreviousToken);
