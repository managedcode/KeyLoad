using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Describes the durable result of a command and its mutation effects.</summary>
/// <param name="CommandId">Identifies the command for deduplication.</param>
/// <param name="Token">Carries the token associated with this operation.</param>
/// <param name="Mutations">Lists committed mutation results or requested mutations.</param>
/// <param name="Durability">Identifies the achieved durability profile.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.CommitReceipt)]
public sealed record CommitReceipt([property: Orleans.Id(0)] Guid CommandId, [property: Orleans.Id(1)] CommitToken Token, [property: Orleans.Id(2)] ImmutableArray<MutationReceipt> Mutations, [property: Orleans.Id(3)] DurabilityProfile Durability);
