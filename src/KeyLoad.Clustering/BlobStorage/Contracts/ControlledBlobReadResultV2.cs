namespace KeyLoad.Clustering;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(ControlledBlobLifetimeReadProtocol.ResultAlias)]
internal sealed record ControlledBlobReadResultV2(
    [property: global::Orleans.Id(0)] object? NativeValue,
    [property: global::Orleans.Id(1)] long ReadBytes,
    [property: global::Orleans.Id(2)] int ExaminedRecords);
