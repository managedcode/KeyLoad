using System.Collections.Immutable;

namespace KeyLoad.Client;

/// <summary>Joined finite worker outcome with bounded identities and actual failures.</summary>
/// <param name="Reason">Local SDK stop cause; no execution success is fabricated.</param>
/// <param name="Received">Actual received count, including known unhandled locally rejected leases.</param>
/// <param name="Acknowledged">Actual successful ACK count.</param>
/// <param name="PeakActiveHandlers">Observed original simultaneous handler ownership.</param>
/// <param name="Operations">Original bounded observations in observed order, including preparation before invocation.</param>
/// <param name="Unacknowledged">Known identities whose ACK was not confirmed; an unknown ACK may already be committed.</param>
/// <param name="Failures">Original failures retained after joined cleanup; application exception graphs are caller-owned and are not CLR-byte measured.</param>
public sealed record QueueWorkerResult(QueueWorkerStopReason Reason, int Received, int Acknowledged,
    int PeakActiveHandlers, ImmutableArray<QueueWorkerOperationObservation> Operations,
    ImmutableArray<QueueWorkerClaimIdentity> Unacknowledged, ImmutableArray<Exception> Failures);
