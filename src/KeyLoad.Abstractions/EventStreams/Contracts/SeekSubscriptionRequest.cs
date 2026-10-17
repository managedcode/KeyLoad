namespace KeyLoad;

/// <summary>Requests moving the checkpoint of a subscription.</summary>
/// <param name="CommandId">The idempotent command identifier.</param>
/// <param name="Subscription">The subscription to seek.</param>
/// <param name="ExpectedGeneration">The subscription generation expected by the caller.</param>
/// <param name="Start">The source position policy to apply.</param>
/// <param name="Cursor">The position cursor when <paramref name="Start"/> is <see cref="SubscriptionStart.FromCursor"/>.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.SeekSubscriptionRequest)]
public sealed record SeekSubscriptionRequest([property: Orleans.Id(0)] Guid CommandId, [property: Orleans.Id(1)] SubscriptionRef Subscription, [property: Orleans.Id(2)] long ExpectedGeneration,
    [property: Orleans.Id(3)] SubscriptionStart Start, [property: Orleans.Id(4)] string? Cursor = null);
