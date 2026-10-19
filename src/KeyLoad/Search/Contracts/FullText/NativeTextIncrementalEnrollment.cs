using KeyLoad.Query.Features.Search;

namespace KeyLoad.Server.Features.Search;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(NativeTextIncrementalAliases.Enrollment)]
internal sealed record NativeTextIncrementalEnrollment(
    [property: global::Orleans.Id(0)] int FormatVersion,
    [property: global::Orleans.Id(1)] Guid BuildCommandId,
    [property: global::Orleans.Id(2)] TextProjectionScope Scope,
    [property: global::Orleans.Id(3)] ProjectionConsumerRef Consumer,
    [property: global::Orleans.Id(4)] long Generation,
    [property: global::Orleans.Id(5)] PhysicalShardRecord Placement,
    [property: global::Orleans.Id(6)] byte[] OriginalRequestSha256,
    [property: global::Orleans.Id(7)] long SourceUpperSequence,
    [property: global::Orleans.Id(8)] string ResourceSha256);
