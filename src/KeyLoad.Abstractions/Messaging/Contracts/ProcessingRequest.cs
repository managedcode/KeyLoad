using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Requests an idempotent handler execution with its declared effects.</summary>
/// <param name="CommandId">Identifies the command for deduplication.</param>
/// <param name="Lane">Identifies the queue lane.</param>
/// <param name="Token">Carries the delivery token.</param>
/// <param name="HandlerScope">Identifies the idempotent handler scope.</param>
/// <param name="ExecutionGeneration">Identifies the handler execution generation.</param>
/// <param name="Effects">Lists mutations committed with handler processing.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.ProcessingRequest)]
public sealed record ProcessingRequest([property: Orleans.Id(0)] Guid CommandId, [property: Orleans.Id(1)] QueueLaneRef Lane, [property: Orleans.Id(2)] string Token, [property: Orleans.Id(3)] string HandlerScope,
    [property: Orleans.Id(4)] long ExecutionGeneration, [property: Orleans.Id(5)] ImmutableArray<Mutation> Effects);
