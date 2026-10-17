namespace KeyLoad.Clustering;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(ControlledBlobReadProtocol.ResultAlias)]
internal sealed record ControlledBlobReadResult(
    [property: global::Orleans.Id(0)] object? NativeValue,
    [property: global::Orleans.Id(1)] bool OutcomeValidated,
    [property: global::Orleans.Id(2)] long ReadBytes,
    [property: global::Orleans.Id(3)] int ExaminedRecords);
