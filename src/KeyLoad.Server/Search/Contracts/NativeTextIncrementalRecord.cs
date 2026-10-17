namespace KeyLoad.Server.Features.Search;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(NativeTextIncrementalAliases.Record)]
internal sealed record NativeTextIncrementalRecord(
    [property: global::Orleans.Id(0)] ulong Id,
    [property: global::Orleans.Id(1)] EntityRef Reference,
    [property: global::Orleans.Id(2)] long Revision,
    [property: global::Orleans.Id(3)] bool Deleted,
    [property: global::Orleans.Id(4)] byte[] CanonicalSha256);

[global::Orleans.GenerateSerializer, global::Orleans.Alias(NativeTextIncrementalAliases.Change)]
internal sealed record NativeTextIncrementalChange(
    [property: global::Orleans.Id(0)] NativeTextIncrementalRecord? Before,
    [property: global::Orleans.Id(1)] NativeTextIncrementalRecord After,
    [property: global::Orleans.Id(2)] NativeTextIncrementalPosting[] Removals,
    [property: global::Orleans.Id(3)] NativeTextIncrementalPosting[] Additions);
