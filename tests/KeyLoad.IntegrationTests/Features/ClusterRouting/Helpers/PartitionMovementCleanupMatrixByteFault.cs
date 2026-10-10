using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Changes one real admitted arm field without creating or accepting a new claim.</summary>
internal static class PartitionMovementCleanupMatrixByteFault
{
    internal static void ReplaceClaimed(RequestCqrsProbeFixture controls, RequestCqrsProbeMarkerRecord primary,
        List<PartitionMovementCleanupMatrixRf3Fault> faults)
    {
        var state = controls.ArmFor(primary.ArmId);
        var original = controls.Json.ReadArm(state.Bytes);
        if (state.Retired || state.Settled || state.DisposedGateJoined || state.RequestId != primary.RequestId
            || original.ArmId != primary.ArmId || original.CommandId != primary.CommandId
            || original.Phase != primary.Phase || !RequestCqrsProbeFixtureProtocol.Nodes.Any(node =>
                RequestCqrsProbeFileNames.OriginForNode(node) == primary.Voter))
        { throw Invalid(); }
        var changedId = Guid.NewGuid();
        if (changedId == original.CommandId)
        { throw Invalid(); }
        var changed = RequestCqrsProbeJsonWriter.Arm(original.SessionId, original.ArmId, original.PrincipalId,
            changedId, original.ReadKind, original.Phase, original.Action, original.Partition?.ToPartition(),
            original.SourceRequestId, original.TargetVoter, original.SourceArmId);
        if (controls.Json.ReadArm(changed) != (original with { CommandId = changedId })
            || changed.AsSpan().SequenceEqual(state.Bytes))
        { throw Invalid(); }
        var name = RequestCqrsProbeFileNames.Arm(primary.ArmId);
        foreach (var node in RequestCqrsProbeFixtureProtocol.Nodes)
        {
            var owned = controls.NodeFor(node);
            RequestCqrsProbeFileStore.VerifyOwnerFile(owned.Directory, owned.OwnerBytes);
            RequestCqrsProbeFileStore.VerifyExactFile(owned.Directory, name, state.Bytes);
            faults.Add(new(node, name, name, changed.ToArray(),
                PartitionMovementCleanupMatrixFaultRepair.RestoreExactBytes, state.Bytes.ToArray()));
            RequestCqrsProbeFileStore.ReplaceExactArm(owned.Directory, name, state.Bytes, changed);
        }
    }
    private static InvalidOperationException Invalid() => new(PartitionMovementCleanupMatrixProtocol.FaultMismatch);
}
