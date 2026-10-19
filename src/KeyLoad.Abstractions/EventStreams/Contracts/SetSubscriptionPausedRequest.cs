namespace KeyLoad;

/// <summary>Requests pausing or resuming a subscription.</summary>
/// <param name="CommandId">The idempotent command identifier.</param>
/// <param name="Subscription">The subscription to update.</param>
/// <param name="ExpectedGeneration">The subscription generation expected by the caller.</param>
/// <param name="Paused">Whether to pause delivery.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.SetSubscriptionPausedRequest)]
public sealed record SetSubscriptionPausedRequest([property: Orleans.Id(0)] Guid CommandId, [property: Orleans.Id(1)] SubscriptionRef Subscription, [property: Orleans.Id(2)] long ExpectedGeneration, [property: Orleans.Id(3)] bool Paused);
