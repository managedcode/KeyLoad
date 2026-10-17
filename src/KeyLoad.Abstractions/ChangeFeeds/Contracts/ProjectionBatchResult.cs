namespace KeyLoad;

/// <summary>Reports the commit result and updated projection checkpoint.</summary>
/// <param name="Receipt">The commit receipt for the projection effects.</param>
/// <param name="AlreadyProcessed">Whether the batch had already been committed.</param>
/// <param name="Checkpoint">The updated consumer checkpoint.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.ProjectionBatchResult)]
public sealed record ProjectionBatchResult([property: Orleans.Id(0)] CommitReceipt Receipt, [property: Orleans.Id(1)] bool AlreadyProcessed, [property: Orleans.Id(2)] long Checkpoint);
