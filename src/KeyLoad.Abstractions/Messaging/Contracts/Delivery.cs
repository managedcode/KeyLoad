namespace KeyLoad;

/// <summary>Returns a leased message and the token and generation needed to process it.</summary>
/// <param name="Id">Identifies the entity, document, key, message, event, or operation.</param>
/// <param name="PayloadJson">Contains the optional message payload.</param>
/// <param name="HeadersJson">Contains optional JSON headers.</param>
/// <param name="Token">Carries the delivery token.</param>
/// <param name="LeaseVersion">Identifies the lease version.</param>
/// <param name="LeaseUntil">Specifies the lease until value.</param>
/// <param name="Attempt">Specifies the attempt value.</param>
/// <param name="DeliveryGeneration">Identifies the delivery generation.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.Delivery)]
public sealed record Delivery([property: Orleans.Id(0)] string Id, [property: Orleans.Id(1)] string PayloadJson, [property: Orleans.Id(2)] string HeadersJson, [property: Orleans.Id(3)] string Token, [property: Orleans.Id(4)] long LeaseVersion,
    [property: Orleans.Id(5)] DateTimeOffset LeaseUntil, [property: Orleans.Id(6)] int Attempt, [property: Orleans.Id(7)] long DeliveryGeneration);
