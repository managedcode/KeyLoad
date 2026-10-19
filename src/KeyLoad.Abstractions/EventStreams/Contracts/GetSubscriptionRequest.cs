namespace KeyLoad;

/// <summary>Identifies the subscription whose status is requested.</summary>
/// <param name="Subscription">The subscription to retrieve.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.GetSubscriptionRequest)]
public sealed record GetSubscriptionRequest([property: Orleans.Id(0)] SubscriptionRef Subscription);
