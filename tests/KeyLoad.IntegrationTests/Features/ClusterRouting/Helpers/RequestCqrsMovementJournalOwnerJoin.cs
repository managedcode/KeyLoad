using Aspire.Hosting;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class RequestCqrsMovementJournalOwnerJoin
{
    private const int FirstArm = 0;
    private const int OneGate = 1;

    internal static void JoinSelected(TwoRf3MembershipWave wave, DistributedApplication originalOwner,
        IReadOnlyList<Guid> originalArms, IReadOnlyDictionary<Guid, RequestCqrsProbeArmState> arms,
        IDictionary<string, int> gates, RequestCqrsProbeJson json, RequestCqrsMovementJournalRecord? proof,
        RequestCqrsProbeMarkerRecord? marker, Task? originalProducer)
    {
        if (originalArms.Count != OneGate || proof is not { } actual || marker is not { } held
            || originalProducer is null || originalArms[FirstArm] != actual.ArmId
            || !arms.TryGetValue(actual.ArmId, out var arm))
        { throw new InvalidOperationException(RequestCqrsProbeFixtureProtocol.UnsettledGates); }
        Join(wave, originalOwner, arm, actual, held, gates, json, originalProducer);
    }

    private static void Join(TwoRf3MembershipWave wave, DistributedApplication originalOwner,
        RequestCqrsProbeArmState arm, RequestCqrsMovementJournalRecord proof,
        RequestCqrsProbeMarkerRecord marker, IDictionary<string, int> gates, RequestCqrsProbeJson json,
        Task originalProducer)
    {
        RequestCqrsMovementJournalInventory.Require(proof);
        RequestCqrsMovementJournalInventory.RequireMarker(proof, marker);
        RequestCqrsMovementJournalInventory.RequireArm(proof, json.ReadArm(arm.Bytes));
        if (!wave.applicationDisposed || !wave.nodeLocksReleased || wave.application is not null
            || !originalProducer.IsCompleted || proof.Error is not null
            || arm.Action != RequestCqrsProbeAction.Hold || arm.Phase != RequestCqrsProbePhase.SubmitReturned
            || arm.ReadKind is not null || arm.RequestId != proof.RequestId || arm.ArmId != proof.ArmId
            || arm.GateVoter is not { } node || arm.Settled || arm.Retired || arm.DisposedGateJoined
            || arm.ProcessOwnerJoined || arm.ProducerDisposedSeen || gates[node] != OneGate
            || gates.Values.Sum() != OneGate)
        { throw new InvalidOperationException(RequestCqrsProbeFixtureProtocol.UnsettledGates); }
        var receipts = wave.RemoteRuntime.RequireKilledOwners(originalOwner);
        if (receipts.Count != TwoRf3MembershipProtocol.NodeCount || !receipts.ContainsKey(node)
            || !TwoRf3MembershipProtocol.Nodes.All(receipts.ContainsKey))
        { throw new InvalidOperationException(RequestCqrsProbeFixtureProtocol.UnsettledGates); }
        gates[node] -= OneGate;
        arm.ProcessOwnerJoined = true;
    }
}
