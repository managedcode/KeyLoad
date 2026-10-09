using Aspire.Hosting;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class RequestCqrsProbeKilledOwnerCleanup
{
    private const int OneGate = 1;
    private const int ExactPair = 2;

    internal static void Join(TwoRf3MembershipWave wave, DistributedApplication originalOwner,
        IReadOnlyList<Guid> originalArms, IReadOnlyDictionary<Guid, RequestCqrsProbeArmState> arms,
        IDictionary<string, int> gates, RequestCqrsProbeJson json)
    {
        if (!wave.applicationDisposed || !wave.nodeLocksReleased || wave.application is not null
            || originalArms.Count != ExactPair || originalArms.Distinct().Count() != originalArms.Count)
        { throw new InvalidOperationException(RequestCqrsProbeFixtureProtocol.UnsettledGates); }
        var receipts = wave.RemoteRuntime.RequireKilledOwners(originalOwner);
        if (!TwoRf3MembershipProtocol.Nodes.All(receipts.ContainsKey)
            || receipts.Count != TwoRf3MembershipProtocol.NodeCount)
        { throw new InvalidOperationException(RequestCqrsProbeFixtureProtocol.UnsettledGates); }
        var selected = originalArms.Select(id => arms.TryGetValue(id, out var arm)
            ? arm : throw new InvalidOperationException(RequestCqrsProbeFixtureProtocol.InvalidArm)).ToArray();
        RequirePair(selected, json);
        foreach (var arm in selected) { Require(arm, receipts, gates); }
        foreach (var group in selected.GroupBy(arm => arm.GateVoter!))
        {
            if (gates[group.Key] < group.Count())
            { throw new InvalidOperationException(RequestCqrsProbeFixtureProtocol.UnsettledGates); }
        }
        foreach (var arm in selected)
        {
            gates[arm.GateVoter!] -= OneGate;
            arm.ProcessOwnerJoined = true;
        }
    }

    private static void RequirePair(RequestCqrsProbeArmState[] selected, RequestCqrsProbeJson json)
    {
        var primary = selected.FirstOrDefault(arm => arm.Phase == KeyLoad.Server.Features.ClusterRouting.RequestCqrsProbePhase.SampleChunkNativeJobReturned);
        var adjunct = selected.FirstOrDefault(arm => arm.Phase == KeyLoad.Server.Features.ClusterRouting.RequestCqrsProbePhase.AuthorizationReload);
        if (selected.Count(arm => arm.Phase == KeyLoad.Server.Features.ClusterRouting.RequestCqrsProbePhase.SampleChunkNativeJobReturned) != OneGate
            || selected.Count(arm => arm.Phase == KeyLoad.Server.Features.ClusterRouting.RequestCqrsProbePhase.AuthorizationReload) != OneGate
            || primary is null || adjunct is null || primary.CommandId != adjunct.CommandId
            || primary.PrincipalId != adjunct.PrincipalId || primary.ReadKind is not null || adjunct.ReadKind is not null
            || primary.RequestId == adjunct.RequestId)
        { throw new InvalidOperationException(RequestCqrsProbeFixtureProtocol.UnsettledGates); }
        var first = json.ReadArm(primary.Bytes);
        var second = json.ReadArm(adjunct.Bytes);
        if (first.SourceArmId is not null || second.SourceArmId != first.ArmId || first.SessionId != second.SessionId
            || !KeyLoad.Server.Features.ClusterRouting.RequestCqrsSampleChunkProbePair.ValidAdjunct(second))
        { throw new InvalidOperationException(RequestCqrsProbeFixtureProtocol.UnsettledGates); }
    }

    private static void Require(RequestCqrsProbeArmState arm,
        IReadOnlyDictionary<string, KeyLoad.IntegrationTests.Features.ClusterReplication.ContainerRuntimeKillReceipt> receipts,
        IDictionary<string, int> gates)
    {
        if (arm.Action != KeyLoad.Server.Features.ClusterRouting.RequestCqrsProbeAction.Hold
            || arm.RequestId is null || arm.GateVoter is not { } node || !receipts.ContainsKey(node)
            || arm.Settled || arm.Retired || arm.DisposedGateJoined || arm.ProcessOwnerJoined
            || arm.ProducerDisposedSeen || arm.Phase == KeyLoad.Server.Features.ClusterRouting.RequestCqrsProbePhase.CanonicalJournalFlushed
            || gates[node] < OneGate)
        { throw new InvalidOperationException(RequestCqrsProbeFixtureProtocol.UnsettledGates); }
    }
}
