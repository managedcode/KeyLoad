namespace KeyLoad.Server.Features.Search;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(TextIncrementalAliases.Record)]
internal sealed record TextIncrementalRecord(
    [property: global::Orleans.Id(0)] ulong Id,
    [property: global::Orleans.Id(1)] EntityRef Reference,
    [property: global::Orleans.Id(2)] long Revision,
    [property: global::Orleans.Id(3)] bool Deleted,
    [property: global::Orleans.Id(4)] byte[] CanonicalSha256);
