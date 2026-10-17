namespace KeyLoad;

/// <summary>Returns the durable target receipt without asserting source acknowledgement.</summary>
/// <param name="Receipt">The original native effects receipt.</param>
/// <param name="AlreadyProcessed">Whether an existing inbox identity supplied the receipt.</param>
/// <param name="OriginalEffectsToken">The original durable target effects cut.</param>
[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(InboxProcessingContractNames.Result)]
public sealed record CommitInboxResult(
    [property: global::Orleans.Id(0)] CommitReceipt Receipt,
    [property: global::Orleans.Id(1)] bool AlreadyProcessed,
    [property: global::Orleans.Id(2)] CommitToken OriginalEffectsToken);
