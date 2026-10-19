using KeyLoad.Query.Features.Search;

namespace KeyLoad.Server.Features.Search;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(TextProtocol.OwnerAlias)]
internal sealed record TextOwnerReceipt(
    [property: global::Orleans.Id(0)] int FormatVersion,
    [property: global::Orleans.Id(1)] string RootDirectory,
    [property: global::Orleans.Id(2)] string GenerationLeaf,
    [property: global::Orleans.Id(3)] Guid SourceNodeId,
    [property: global::Orleans.Id(4)] TextProjectionScope? Scope,
    [property: global::Orleans.Id(5)] TextOwnedPath[] OwnedPaths);
