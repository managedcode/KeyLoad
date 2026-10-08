namespace KeyLoad.Orleans;

/// <summary>Original stored result and actual native grant metrics from the authenticated read.</summary>
[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(PartitionMovementAliases.OutcomeWitness)]
internal sealed record PartitionMovementOutcomeWitness(
    [property: global::Orleans.Id(0)] OperationResult Result,
    [property: global::Orleans.Id(1)] long ReadBytes,
    [property: global::Orleans.Id(2)] int ExaminedRecords);
