using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>The atomic filename fault preserves the actual previously admitted control bytes.</summary>
internal sealed record PartitionMovementCleanupMatrixRf3Fault(string Node, string OriginalName,
    string FaultName, byte[] ExactBytes,
    PartitionMovementCleanupMatrixFaultRepair Repair = PartitionMovementCleanupMatrixFaultRepair.RenameBack,
    byte[]? OriginalBytes = null)
{
    internal static void RenameAdmittedArm(RequestCqrsProbeFixture controls,
        Guid admittedArm, RequestCqrsProbeMarkerRecord actualPrimary, List<PartitionMovementCleanupMatrixRf3Fault> ownedFaults)
    {
        if (!RequestCqrsProbeFixtureProtocol.Nodes.Any(name =>
            RequestCqrsProbeFileNames.OriginForNode(name) == actualPrimary.Voter))
        { throw Invalid(); }
        foreach (var node in RequestCqrsProbeFixtureProtocol.Nodes)
        { RenameOne(controls, admittedArm, actualPrimary, node, ownedFaults); }
    }

    private static void RenameOne(RequestCqrsProbeFixture controls,
        Guid admittedArm, RequestCqrsProbeMarkerRecord actualPrimary, string node,
        List<PartitionMovementCleanupMatrixRf3Fault> ownedFaults)
    {
        var owned = controls.NodeFor(node);
        RequestCqrsProbeFileStore.VerifyOwnerFile(owned.Directory, owned.OwnerBytes);
        var state = controls.ArmFor(admittedArm);
        if (state.Retired || state.Settled || state.DisposedGateJoined
            || (admittedArm == actualPrimary.ArmId
                ? state.RequestId != actualPrimary.RequestId
                : state.RequestId is not null))
        { throw Invalid(); }
        var original = RequestCqrsProbeFileNames.Arm(admittedArm);
        RequestCqrsProbeFileStore.VerifyExactFile(owned.Directory, original, state.Bytes);
        var faultId = Guid.NewGuid();
        if (faultId == admittedArm)
        { throw Invalid(); }
        var changed = RequestCqrsProbeFileNames.Arm(faultId);
        RequestCqrsProbeFileStore.EnsureCanWrite(owned.Directory, changed, state.Bytes.Length);
        ownedFaults.Add(new(node, original, changed, state.Bytes.ToArray()));
        File.Move(Path.Combine(owned.Directory, original), Path.Combine(owned.Directory, changed), overwrite: false);
        RequestCqrsProbeFileStore.VerifyExactFile(owned.Directory, changed, state.Bytes);
    }

    internal static void RenameObservedControl(RequestCqrsProbeFixture controls,
        RequestCqrsProbeMarkerRecord primary, List<PartitionMovementCleanupMatrixRf3Fault> ownedFaults)
    {
        var node = RequestCqrsProbeFixtureProtocol.Nodes.Single(name =>
            RequestCqrsProbeFileNames.OriginForNode(name) == primary.Voter);
        var owned = controls.NodeFor(node);
        RequestCqrsProbeFileStore.VerifyOwnerFile(owned.Directory, owned.OwnerBytes);
        var original = RequestCqrsProbeFileNames.Marker(primary);
        var bytes = RequestCqrsProbeFileStore.ReadRecord(Path.Combine(owned.Directory, original));
        if (controls.Json.ReadMarker(bytes) != primary)
        { throw Invalid(); }
        var prefix = RequestCqrsProbeFixtureProtocol.MarkerFilePrefix;
        var newId = Guid.NewGuid();
        if (newId == primary.ArmId)
        { throw Invalid(); }
        var changed = prefix + newId.ToString(RequestCqrsProbeProtocol.SessionIdFormat)
            + original[(prefix.Length + PartitionMovementCleanupMatrixProtocol.CanonicalGuidDigits)..];
        if (!RequestCqrsProbeFileNames.IsMarkerName(changed))
        { throw Invalid(); }
        RequestCqrsProbeFileStore.EnsureCanWrite(owned.Directory, changed, bytes.Length);
        ownedFaults.Add(new(node, original, changed, bytes.ToArray()));
        File.Move(Path.Combine(owned.Directory, original), Path.Combine(owned.Directory, changed), overwrite: false);
        RequestCqrsProbeFileStore.VerifyExactFile(owned.Directory, changed, bytes);
    }

    internal static void ResurrectRetired(RequestCqrsProbeFixture controls, Guid retiredOther,
        RequestCqrsProbeMarkerRecord actualAdjunct, List<PartitionMovementCleanupMatrixRf3Fault> ownedFaults)
    {
        var node = RequestCqrsProbeFixtureProtocol.Nodes.Single(name =>
            RequestCqrsProbeFileNames.OriginForNode(name) == actualAdjunct.Voter);
        var owned = controls.NodeFor(node);
        RequestCqrsProbeFileStore.VerifyOwnerFile(owned.Directory, owned.OwnerBytes);
        var retired = controls.ArmFor(retiredOther);
        if (!retired.Retired || retired.RequestId is not null)
        { throw Invalid(); }
        var name = RequestCqrsProbeFileNames.Arm(retiredOther);
        RequestCqrsProbeFileStore.EnsureCanWrite(owned.Directory, name, retired.Bytes.Length);
        ownedFaults.Add(new(node, name, name, retired.Bytes.ToArray(),
            PartitionMovementCleanupMatrixFaultRepair.RemoveResurrection));
        RequestCqrsProbeFileStore.WriteAtomic(owned.Directory, name, retired.Bytes);
        RequestCqrsProbeFileStore.VerifyExactFile(owned.Directory, name, retired.Bytes);
    }

    internal void RestoreAfterJoin(TwoRf3MembershipWave wave, RequestCqrsProbeFixture controls)
    {
        if (!wave.applicationDisposed || !wave.nodeLocksReleased)
        { throw Invalid(); }
        wave.AssertAllNodeLocksReleased();
        var owned = controls.NodeFor(Node);
        RequestCqrsProbeFileStore.VerifyOwnerFile(owned.Directory, owned.OwnerBytes);
        RequestCqrsProbeFileStore.VerifyExactFile(owned.Directory, FaultName, ExactBytes);
        if (Repair == PartitionMovementCleanupMatrixFaultRepair.RestoreExactBytes)
        {
            if (OriginalName != FaultName || OriginalBytes is null)
            { throw Invalid(); }
            RequestCqrsProbeFileStore.ReplaceExactArm(owned.Directory, OriginalName, ExactBytes, OriginalBytes);
            return;
        }
        if (Repair == PartitionMovementCleanupMatrixFaultRepair.RemoveUnfamiliarCopy)
        {
            if (OriginalName == FaultName)
            { throw Invalid(); }
            RequestCqrsProbeFileStore.VerifyExactFile(owned.Directory, OriginalName, ExactBytes);
            RequestCqrsProbeFileStore.DeleteExactFile(owned.Directory, FaultName, ExactBytes);
            RequestCqrsProbeFileStore.VerifyExactFile(owned.Directory, OriginalName, ExactBytes);
            return;
        }
        if (Repair == PartitionMovementCleanupMatrixFaultRepair.RemoveResurrection)
        {
            if (OriginalName != FaultName)
            { throw Invalid(); }
            RequestCqrsProbeFileStore.DeleteExactFile(owned.Directory, FaultName, ExactBytes);
            return;
        }
        if (Repair != PartitionMovementCleanupMatrixFaultRepair.RenameBack)
        { throw Invalid(); }
        RequestCqrsProbeFileStore.EnsureCanWrite(owned.Directory, OriginalName, ExactBytes.Length);
        File.Move(Path.Combine(owned.Directory, FaultName), Path.Combine(owned.Directory, OriginalName), overwrite: false);
        RequestCqrsProbeFileStore.VerifyExactFile(owned.Directory, OriginalName, ExactBytes);
    }
    private static InvalidOperationException Invalid() => new(PartitionMovementCleanupMatrixProtocol.FaultMismatch);
}
