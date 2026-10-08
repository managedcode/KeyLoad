namespace KeyLoad.Orleans;

/// <summary>Exact original capability and ordinal; no caller-selected partition or raw storage prefix.</summary>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(PartitionMovementAliases.PageQuery)]
internal sealed record PartitionMovementPageQuery(
    [property: global::Orleans.Id(0)] Guid HandleId,
    [property: global::Orleans.Id(1)] int Ordinal);
