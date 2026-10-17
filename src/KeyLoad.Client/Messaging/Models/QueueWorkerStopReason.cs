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
    /// <summary>Actual native receive refused.</summary>
    ReceiveRefused,
    /// <summary>Original native receive remains uncertain.</summary>
    ReceiveUncertain,
    /// <summary>Returned active UTF8 body/header bytes exceeded the caller allowance.</summary>
    InFlightBytes,
    /// <summary>Actual native renewal refused or remained uncertain.</summary>
    RenewalFailed,
    /// <summary>Actual native ACK refused or remained uncertain.</summary>
    AcknowledgementFailed,
    /// <summary>Original handler failed.</summary>
    HandlerFailed,
    /// <summary>Actual native successful reply violated the requested identity or count bound.</summary>
    NativeReplyRejected,
    /// <summary>Original native or owned lifecycle failure, retained after joined shutdown.</summary>
    LifecycleFailed,
    /// <summary>Caller cancellation settled every owned task; leases remain server-owned.</summary>
    Cancelled
}
