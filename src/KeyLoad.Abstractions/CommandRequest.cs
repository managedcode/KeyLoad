using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Carries one identified command, its mutations, and partition ownership epoch.</summary>
/// <param name="CommandId">Identifies the command for deduplication.</param>
/// <param name="Partition">Identifies the atomic partition containing the resource.</param>
/// <param name="Mutations">Lists committed mutation results or requested mutations.</param>
/// <param name="OwnershipEpoch">Identifies the active ownership epoch.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.CommandRequest)]
public sealed record CommandRequest([property: Orleans.Id(0)] Guid CommandId, [property: Orleans.Id(1)] PartitionRef Partition, [property: Orleans.Id(2)] ImmutableArray<Mutation> Mutations, [property: Orleans.Id(3)] long OwnershipEpoch = CommandRequest.DefaultOwnershipEpoch)
{
    private const int DefaultOwnershipEpoch = 1;
}
