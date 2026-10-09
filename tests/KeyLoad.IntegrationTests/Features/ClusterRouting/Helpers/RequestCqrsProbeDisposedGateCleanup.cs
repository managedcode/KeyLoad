using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Actual producer disposal joins a failed gate for cleanup without manufacturing cancellation or release.</summary>
internal static class RequestCqrsProbeDisposedGateCleanup
{
    private const int OneGate = 1;
    internal static void Join(RequestCqrsProbeArmState arm, IDictionary<string, int> gates, Task originalProducer)
    {
        if (!originalProducer.IsCompleted || arm.RequestId is null || !arm.ProducerDisposedSeen
            || arm.Phase == RequestCqrsProbePhase.CanonicalJournalFlushed || arm.Action != RequestCqrsProbeAction.Hold
            || arm.GateVoter is not { } voter || arm.Settled || arm.Retired)
        { throw new InvalidOperationException(RequestCqrsProbeFixtureProtocol.UnsettledGates); }
        if (arm.DisposedGateJoined) { return; }
        if (gates[voter] < OneGate) { throw new InvalidOperationException(RequestCqrsProbeFixtureProtocol.UnsettledGates); }
        gates[voter] -= OneGate;
        arm.DisposedGateJoined = true;
    }
}
