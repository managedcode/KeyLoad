namespace KeyLoad.Server.Search;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(TextIncrementalAliases.Change)]
internal sealed record TextIncrementalChange(
    [property: global::Orleans.Id(0)] TextIncrementalRecord? Before,
    [property: global::Orleans.Id(1)] TextIncrementalRecord After,
    [property: global::Orleans.Id(2)] TextIncrementalPosting[] Removals,
    [property: global::Orleans.Id(3)] TextIncrementalPosting[] Additions);
