namespace KeyLoad.Client;

/// <summary>One actual delivery and its original local completion identities.</summary>
/// <param name="Lane">Authenticated execution queue lane.</param>
/// <param name="Delivery">Actual committed delivery; no local token reconstruction.</param>
/// <param name="AcknowledgementId">Original ACK identity retained for uncertainty resolution.</param>
/// <param name="FirstRenewalId">Original first-renewal identity, never re-created on retry.</param>
public sealed record QueueWorkerDeliveryContext(QueueLaneRef Lane, Delivery Delivery,
    Guid AcknowledgementId, Guid FirstRenewalId);
