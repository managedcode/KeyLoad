namespace KeyLoad.Server.Features.Search;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(NativeTextIncrementalAliases.Change)]
internal sealed record NativeTextIncrementalChange(
    [property: global::Orleans.Id(0)] NativeTextIncrementalRecord? Before,
    [property: global::Orleans.Id(1)] NativeTextIncrementalRecord After,
    [property: global::Orleans.Id(2)] NativeTextIncrementalPosting[] Removals,
    [property: global::Orleans.Id(3)] NativeTextIncrementalPosting[] Additions);
