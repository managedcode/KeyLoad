namespace KeyLoad;

/// <summary>Requests a bounded batch of subscription deliveries.</summary>
/// <param name="RequestId">The idempotent request identifier.</param>
/// <param name="Subscription">The subscription to receive from.</param>
/// <param name="MaxEvents">The maximum number of events to return.</param>
/// <param name="MaxBytes">The maximum payload bytes to return.</param>
/// <param name="LeaseSeconds">The requested delivery lease duration in seconds.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.ReceiveSubscriptionRequest)]
public sealed record ReceiveSubscriptionRequest([property: Orleans.Id(0)] Guid RequestId, [property: Orleans.Id(1)] SubscriptionRef Subscription, [property: Orleans.Id(2)] int MaxEvents = ReceiveSubscriptionRequest.DefaultMaxEvents,
    [property: Orleans.Id(3)] int MaxBytes = ReceiveSubscriptionRequest.DefaultMaxBytes, [property: Orleans.Id(4)] int LeaseSeconds = ReceiveSubscriptionRequest.DefaultLeaseSeconds)
{
    private const int DefaultMaxEvents = 1;
    private const int DefaultMaxBytes = 1_048_576;
    private const int DefaultLeaseSeconds = 30;
}
