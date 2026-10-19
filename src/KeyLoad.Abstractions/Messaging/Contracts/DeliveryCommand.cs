namespace KeyLoad;

/// <summary>Requests acknowledgement, rejection, or lease renewal for a delivery.</summary>
/// <param name="CommandId">Identifies the command for deduplication.</param>
/// <param name="Lane">Identifies the queue lane.</param>
/// <param name="Token">Carries the delivery token.</param>
/// <param name="Action">Selects the delivery action.</param>
/// <param name="LeaseSeconds">Requests the message lease duration.</param>
/// <param name="FailureCode">Provides a safe handler failure code.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.DeliveryCommand)]
public sealed record DeliveryCommand([property: Orleans.Id(0)] Guid CommandId, [property: Orleans.Id(1)] QueueLaneRef Lane, [property: Orleans.Id(2)] string Token, [property: Orleans.Id(3)] DeliveryAction Action,
    [property: Orleans.Id(4)] int? LeaseSeconds = null, [property: Orleans.Id(5)] string? FailureCode = null);
