namespace KeyLoad;

/// <summary>Selects the acknowledgement action for a delivery.</summary>
public enum DeliveryAction
{
    /// <summary>Acknowledge the delivery.</summary>
    Ack,
    /// <summary>Reject the delivery and apply retry policy.</summary>
    Nack,
    /// <summary>Extend the delivery lease.</summary>
    Renew
}
