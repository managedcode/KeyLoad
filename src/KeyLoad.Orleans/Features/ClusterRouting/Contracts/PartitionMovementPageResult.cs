namespace KeyLoad.Orleans;

/// <summary>Owned bounded native page bytes; no borrowed canonical storage or image page escapes.</summary>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(PartitionMovementAliases.PageResult)]
internal sealed record PartitionMovementPageResult(
    [property: global::Orleans.Id(0)] Guid HandleId,
    [property: global::Orleans.Id(1)] int Ordinal,
    [property: global::Orleans.Id(2)] ReadOnlyMemory<byte> NativePage);
