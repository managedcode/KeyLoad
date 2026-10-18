using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Describes the durable result of a command and its mutation effects.</summary>
/// <param name="CommandId">Identifies the command for deduplication.</param>
/// <param name="Token">Carries the token associated with this operation.</param>
/// <param name="Mutations">Lists committed mutation results or requested mutations.</param>
/// <param name="Durability">Identifies the achieved durability boundary on the acknowledged copies.</param>
/// <param name="PersistedCopies">The actual number of storage copies that acknowledged this command.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.CommitReceipt)]
public sealed record CommitReceipt([property: Orleans.Id(0)] Guid CommandId, [property: Orleans.Id(1)] CommitToken Token, [property: Orleans.Id(2)] ImmutableArray<MutationReceipt> Mutations, [property: Orleans.Id(3)] DurabilityProfile Durability,
    [property: Orleans.Id(4)] int PersistedCopies = CommitReceipt.LocalCopyCount)
{
    internal const int LocalCopyCount = 1;
}

