namespace KeyLoad;

/// <summary>Explicitly bounds lifetime retained target-inbox receipts without a pruning default.</summary>
/// <param name="MaxReceipts">The positive configured maximum receipt count.</param>
/// <param name="MaxBytes">The positive configured maximum retained key and record bytes.</param>
[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(InboxProcessingContractNames.Policy)]
public sealed record InboxPolicy(
    [property: global::Orleans.Id(0)] long MaxReceipts,
    [property: global::Orleans.Id(1)] long MaxBytes);
