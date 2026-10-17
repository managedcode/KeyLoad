namespace KeyLoad.Replication;

/// <summary>Fixed numeric metadata allowance borrowed from one original source operation.</summary>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(ReplicaForwardReadBudget.GrantAlias)]
internal sealed record ReplicaForwardReadGrantV1(
    [property: global::Orleans.Id(0)] long MaximumReadBytes,
    [property: global::Orleans.Id(1)] int MaximumExaminedRecords,
    [property: global::Orleans.Id(2)] int MaximumResultBytes);
