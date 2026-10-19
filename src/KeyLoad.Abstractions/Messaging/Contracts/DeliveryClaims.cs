namespace KeyLoad;

/// <summary>Binds a delivery token to its lane, principal, lease, and incarnation.</summary>
/// <param name="Lane">Identifies the queue lane.</param>
/// <param name="MessageId">Identifies the queue message.</param>
/// <param name="PrincipalId">Identifies the principal receiving the delivery.</param>
/// <param name="LeaseVersion">Identifies the lease version.</param>
/// <param name="DeliveryGeneration">Identifies the delivery generation.</param>
/// <param name="Incarnation">Identifies the issuing storage incarnation.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.DeliveryClaims)]
public sealed record DeliveryClaims([property: Orleans.Id(0)] QueueLaneRef Lane, [property: Orleans.Id(1)] string MessageId, [property: Orleans.Id(2)] string PrincipalId, [property: Orleans.Id(3)] long LeaseVersion,
    [property: Orleans.Id(4)] long DeliveryGeneration, [property: Orleans.Id(5)] Guid Incarnation);
