namespace KeyLoad;

/// <summary>Requests an action on a leased subscription delivery.</summary>
/// <param name="CommandId">The idempotent command identifier.</param>
/// <param name="Subscription">The subscription owning the delivery.</param>
/// <param name="Token">The delivery acknowledgement token.</param>
/// <param name="Action">The action to apply to the delivery.</param>
/// <param name="LeaseSeconds">An optional replacement lease duration in seconds.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.SubscriptionDeliveryCommand)]
public sealed record SubscriptionDeliveryCommand([property: Orleans.Id(0)] Guid CommandId, [property: Orleans.Id(1)] SubscriptionRef Subscription, [property: Orleans.Id(2)] string Token, [property: Orleans.Id(3)] DeliveryAction Action,
    [property: Orleans.Id(4)] int? LeaseSeconds = null);
