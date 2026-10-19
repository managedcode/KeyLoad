namespace KeyLoad;

/// <summary>Reports the committed result of subscription event processing.</summary>
/// <param name="Receipt">The commit receipt for the processing effects.</param>
/// <param name="AlreadyProcessed">Whether the event had already been processed.</param>
/// <param name="OriginalEffectsToken">The token of the original processing effects.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.SubscriptionProcessingResult)]
public sealed record SubscriptionProcessingResult([property: Orleans.Id(0)] CommitReceipt Receipt, [property: Orleans.Id(1)] bool AlreadyProcessed, [property: Orleans.Id(2)] CommitToken OriginalEffectsToken);
