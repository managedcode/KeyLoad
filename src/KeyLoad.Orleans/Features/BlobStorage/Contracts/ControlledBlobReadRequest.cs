using KeyLoad.Core.Features.BlobStorage;

namespace KeyLoad.Orleans;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(ControlledBlobReadProtocol.RequestAlias)]
internal sealed record ControlledBlobReadRequest(
    [property: global::Orleans.Id(0)] ControlledBlobReadFrame Frame,
    [property: global::Orleans.Id(1)] long MaximumReadBytes,
    [property: global::Orleans.Id(2)] int MaximumExaminedRecords,
    [property: global::Orleans.Id(3)] int MaximumResultBytes);

[global::Orleans.GenerateSerializer, global::Orleans.Alias(ControlledBlobReadProtocol.ResultAlias)]
internal sealed record ControlledBlobReadResult(
    [property: global::Orleans.Id(0)] object? NativeValue,
    [property: global::Orleans.Id(1)] bool OutcomeValidated,
    [property: global::Orleans.Id(2)] long ReadBytes,
    [property: global::Orleans.Id(3)] int ExaminedRecords);
