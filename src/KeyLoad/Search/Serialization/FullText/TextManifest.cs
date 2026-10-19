using KeyLoad.Query.Features.Search;

namespace KeyLoad.Server.Features.Search;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(TextProtocol.ManifestAlias)]
internal sealed record TextManifest(
    [property: global::Orleans.Id(0)] int FormatVersion,
    [property: global::Orleans.Id(1)] TextProjectionScope Scope,
    [property: global::Orleans.Id(2)] string TokenizerVersion,
    [property: global::Orleans.Id(3)] string HashVersion,
    [property: global::Orleans.Id(4)] TextRecord[] Records,
    [property: global::Orleans.Id(5)] TextFile[] Files);
