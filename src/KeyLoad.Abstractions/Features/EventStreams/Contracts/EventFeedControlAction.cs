namespace KeyLoad;

/// <summary>Canonical whole-map operation or bounded native tail operation.</summary>
public enum EventFeedControlAction
{
    /// <summary>Admit complete source coverage and original retention pins.</summary>
    Open = 0,
    /// <summary>Persist one complete unacknowledged page.</summary>
    Offer = 1,
    /// <summary>Advance exactly the complete original offered vector.</summary>
    Acknowledge = 2,
    /// <summary>Reconcile complete current source coverage.</summary>
    Refresh = 3,
    /// <summary>Settle actual source dispositions and release the map.</summary>
    Release = 4,
    /// <summary>Register advisory wakeups and consume bounded canonical offers.</summary>
    Tail = 5
}
