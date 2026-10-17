namespace KeyLoad.Replication;

/// <summary>Actual joined receiver metadata work and exact encoded original-result payload length.</summary>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(ReplicaForwardReadBudget.StatsAlias)]
internal sealed record ReplicaForwardReadStatsV1(
    [property: global::Orleans.Id(0)] long ReadBytes,
    [property: global::Orleans.Id(1)] int ExaminedRecords,
    [property: global::Orleans.Id(2)] int ResultBytes);
