namespace KeyLoad;

/// <summary>Identifies a committed position and ownership epoch for an atomic partition.</summary>
/// <param name="Incarnation">Identifies the issuing storage incarnation.</param>
/// <param name="AtomicPartitionId">Identifies the atomic partition.</param>
/// <param name="Position">Identifies the committed position.</param>
/// <param name="OwnershipEpoch">Identifies the active ownership epoch.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.CommitToken)]
public sealed record CommitToken([property: Orleans.Id(0)] Guid Incarnation, [property: Orleans.Id(1)] string AtomicPartitionId, [property: Orleans.Id(2)] long Position, [property: Orleans.Id(3)] long OwnershipEpoch);
