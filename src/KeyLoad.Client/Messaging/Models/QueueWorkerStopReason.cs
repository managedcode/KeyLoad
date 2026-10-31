namespace KeyLoad.Client;

/// <summary>Local SDK lifecycle outcomes; these are not server wire operation kinds.</summary>
public enum QueueWorkerStopReason
{
    /// <summary>Finite delivery admission completed and all owned tasks joined.</summary>
    DeliveryBudget,
    /// <summary>Finite receive calls completed and all owned tasks joined.</summary>
    ReceiveBudget,
    /// <summary>Finite renewal calls required cancellation and joined settlement.</summary>
    RenewalBudget,
    /// <summary>Actual execution receive refused.</summary>
    ReceiveRefused,
    /// <summary>Original execution receive remains uncertain.</summary>
    ReceiveUncertain,
    /// <summary>Actual execution renewal refused or remained uncertain.</summary>
    RenewalFailed,
    /// <summary>Actual execution ACK refused or remained uncertain.</summary>
    AcknowledgementFailed,
    /// <summary>Original handler failed.</summary>
    HandlerFailed,
    /// <summary>Actual execution successful reply violated the requested identity or count bound.</summary>
    RuntimeReplyRejected,
    /// <summary>Original execution or owned lifecycle failure, retained after joined shutdown.</summary>
    LifecycleFailed,
    /// <summary>Caller cancellation settled every owned task; leases remain server-owned.</summary>
    Cancelled
}
