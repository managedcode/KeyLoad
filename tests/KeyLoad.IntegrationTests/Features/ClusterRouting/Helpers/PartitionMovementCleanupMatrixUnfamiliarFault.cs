using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Copies genuine controls without changing or admitting any record or claim.</summary>
internal static class PartitionMovementCleanupMatrixUnfamiliarFault
{
    internal static void CopyActual(RequestCqrsProbeFixture controls, Guid retiredOther,
        RequestCqrsProbeMarkerRecord primary, RequestCqrsProbeMarkerRecord released,
        RequestCqrsProbeMarkerRecord adjunct, PartitionMovementCleanupMatrixFaultRole role,
        List<PartitionMovementCleanupMatrixRf3Fault> faults)
    {
        var node = RequestCqrsProbeFixtureProtocol.Nodes.Single(name =>
            RequestCqrsProbeFileNames.OriginForNode(name) == adjunct.Voter);
        var owned = controls.NodeFor(node);
        RequestCqrsProbeFileStore.VerifyOwnerFile(owned.Directory, owned.OwnerBytes);
        var unfamiliar = Guid.NewGuid();
        if (unfamiliar == Guid.Empty || unfamiliar == retiredOther
            || unfamiliar == primary.ArmId || unfamiliar == adjunct.ArmId)
        { throw Invalid(); }
        var names = Names(primary, role, unfamiliar);
        var bytes = RequestCqrsProbeFileStore.ReadRecord(Path.Combine(owned.Directory, names.Original));
        RequireActual(controls, primary, released, role, bytes);
        RequestCqrsProbeFileStore.VerifyExactFile(owned.Directory, names.Original, bytes);
        RequestCqrsProbeFileStore.EnsureCanWrite(owned.Directory, names.Copy, bytes.Length);
        faults.Add(new(node, names.Original, names.Copy, bytes.ToArray(),
            PartitionMovementCleanupMatrixFaultRepair.RemoveUnfamiliarCopy));
        RequestCqrsProbeFileStore.WriteAtomic(owned.Directory, names.Copy, bytes);
        RequestCqrsProbeFileStore.VerifyExactFile(owned.Directory, names.Copy, bytes);
        RequestCqrsProbeFileStore.VerifyExactFile(owned.Directory, names.Original, bytes);
    }

    private static (string Original, string Copy) Names(RequestCqrsProbeMarkerRecord primary,
        PartitionMovementCleanupMatrixFaultRole role, Guid unfamiliar)
    {
        if (role == PartitionMovementCleanupMatrixFaultRole.UnfamiliarRelease)
        {
            return (RequestCqrsProbeFileNames.Release(primary.ArmId, primary.RequestId),
            RequestCqrsProbeFileNames.Release(unfamiliar, primary.RequestId));
        }
        if (role != PartitionMovementCleanupMatrixFaultRole.UnfamiliarMarker)
        { throw Invalid(); }
        var original = RequestCqrsProbeFileNames.Marker(primary);
        var prefix = RequestCqrsProbeFixtureProtocol.MarkerFilePrefix;
        var copy = prefix + unfamiliar.ToString(RequestCqrsProbeProtocol.SessionIdFormat)
            + original[(prefix.Length + PartitionMovementCleanupMatrixProtocol.CanonicalGuidDigits)..];
        if (!RequestCqrsProbeFileNames.IsMarkerName(copy))
        { throw Invalid(); }
        return (original, copy);
    }

    private static void RequireActual(RequestCqrsProbeFixture controls, RequestCqrsProbeMarkerRecord primary,
        RequestCqrsProbeMarkerRecord released, PartitionMovementCleanupMatrixFaultRole role, byte[] bytes)
    {
        if (role == PartitionMovementCleanupMatrixFaultRole.UnfamiliarMarker)
        {
            if (controls.Json.ReadMarker(bytes) != primary)
            { throw Invalid(); }
            return;
        }
        var release = controls.Json.ReadRelease(bytes);
        if (role != PartitionMovementCleanupMatrixFaultRole.UnfamiliarRelease
            || release.SessionId != primary.SessionId || release.ArmId != primary.ArmId
            || release.RequestId != primary.RequestId || released.ArmId != primary.ArmId
            || released.RequestId != primary.RequestId || released.Outcome != RequestCqrsProbeOutcome.Released)
        { throw Invalid(); }
    }

    private static InvalidOperationException Invalid() => new(PartitionMovementCleanupMatrixProtocol.FaultMismatch);
}
