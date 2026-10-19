namespace KeyLoad;

/// <summary>Represents one leased event delivery.</summary>
/// <param name="Event">The delivered event.</param>
/// <param name="Token">The acknowledgement token for this delivery.</param>
/// <param name="LeaseVersion">The lease version used to reject stale acknowledgements.</param>
/// <param name="LeaseUntil">The lease expiration time.</param>
/// <param name="Attempt">The one-based delivery attempt number.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.SubscriptionDelivery)]
public sealed record SubscriptionDelivery([property: Orleans.Id(0)] SourceEventRecord Event, [property: Orleans.Id(1)] string Token, [property: Orleans.Id(2)] long LeaseVersion, [property: Orleans.Id(3)] DateTimeOffset LeaseUntil, [property: Orleans.Id(4)] int Attempt);
