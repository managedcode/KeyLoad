namespace KeyLoad.Server.Features.Search;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(NativeTextProtocol.RecordAlias)]
internal sealed record NativeTextRecord(
    [property: global::Orleans.Id(0)] ulong Id,
    [property: global::Orleans.Id(1)] EntityRef Reference,
    [property: global::Orleans.Id(2)] long Revision);
