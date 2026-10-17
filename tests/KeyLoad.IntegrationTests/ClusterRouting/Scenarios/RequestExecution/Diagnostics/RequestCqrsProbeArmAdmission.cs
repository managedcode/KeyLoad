using KeyLoad.Clustering;
using KeyLoad.Server.Features.ClusterRouting;
using static KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsProbeFixtureProtocol;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class RequestCqrsProbeArmAdmission
{
    internal static Guid WriteArm(bool admissionStopped, bool cleaned,
        IDictionary<Guid, RequestCqrsProbeArmState> arms,
        string sessionId, RequestCqrsProbeJson json, IReadOnlyList<string> nodes,
        IReadOnlyDictionary<string, string> nodeDirectories, IReadOnlyDictionary<string, byte[]> ownerRecords,
        string principalId, Guid commandId, GrainReadKind? readKind, RequestCqrsProbePhase phase,
        RequestCqrsProbeAction action, PartitionRef? partition, Guid? sourceRequestId, string? targetVoter, Guid? sourceArmId)
    {
        if (admissionStopped || cleaned)
        {
            throw new InvalidOperationException(AdmissionStopped);
        }
        RequestCqrsProbeArmValidator.Validate(principalId, commandId, readKind, phase, action);
        if (arms.Count >= MaximumArms)
        {
            throw new IOException(FileLimitExceeded);
        }
        var armId = Guid.NewGuid();
        while (arms.ContainsKey(armId))
        {
            armId = Guid.NewGuid();
        }
        var bytes = RequestCqrsProbeJsonWriter.Arm(sessionId, armId, principalId, commandId, readKind, phase, action, partition, sourceRequestId, targetVoter, sourceArmId);
        var decoded = json.ReadArm(bytes);
        RequestCqrsProbeArmValidator.ValidateDecoded(decoded, armId, principalId, commandId, readKind, phase, action);
        if (decoded.Partition?.ToPartition() != partition || decoded.SourceRequestId != sourceRequestId || decoded.TargetVoter != targetVoter || decoded.SourceArmId != sourceArmId)
        {
            throw new InvalidOperationException(InvalidArm);
        }
        RequestCqrsProbeBroadcast.WriteIdentical(nodes, nodeDirectories, ownerRecords,
            RequestCqrsProbeFileNames.Arm(armId), bytes);
        var state = new RequestCqrsProbeArmState(principalId, commandId, readKind, phase, action, bytes)
        {
            ArmId = armId
        };
        arms.Add(armId, state);
        return armId;
    }

    internal static void WriteReplayPlan(bool admissionStopped, bool cleaned,
        IDictionary<Guid, RequestCqrsProbeArmState> arms,
        string sessionId, RequestCqrsProbeJson json, IReadOnlyList<string> nodes,
        IReadOnlyDictionary<string, string> nodeDirectories, IReadOnlyDictionary<string, byte[]> ownerRecords,
        Guid armId, KeyLoad.Server.Features.DocumentStorage.RemoteModelNativeReplayVariation mode)
    {
        if (admissionStopped || cleaned || !arms.TryGetValue(armId, out var arm) || arm.Retired
            || arm.RequestId is not null || arm.Phase != RequestCqrsProbePhase.NativeModelReplayBeforeDispatch)
        {
            throw new InvalidOperationException(InvalidArm);
        }
        var plan = new RequestCqrsNativeReplayPlan(RequestCqrsProbeProtocol.Version,
            RequestCqrsNativeReplayInventory.PlanKind, sessionId, armId, arm.CommandId, arm.PrincipalId, mode);
        RequestCqrsNativeReplayInventory.RequireArm(plan, json.ReadArm(arm.Bytes));
        var bytes = RequestCqrsNativeReplayInventory.WritePlan(plan);
        RequestCqrsProbeBroadcast.WriteIdentical(nodes, nodeDirectories, ownerRecords,
            RequestCqrsNativeReplayInventory.PlanName(armId), bytes);
    }

    internal static void WriteRelease(IDictionary<Guid, RequestCqrsProbeArmState> arms,
        string sessionId, RequestCqrsProbeJson json, IReadOnlyList<string> nodes,
        IReadOnlyDictionary<string, string> nodeDirectories, IReadOnlyDictionary<string, byte[]> ownerRecords,
        Guid armId, Guid requestId)
    {
        if (!arms.TryGetValue(armId, out var arm) || arm.Action != RequestCqrsProbeAction.Hold
            || arm.Retired || arm.Settled || arm.DisposedGateJoined || arm.ProcessOwnerJoined || requestId == Guid.Empty
            || arm.RequestId != requestId || arm.ReleaseWritten)
        {
            throw new InvalidOperationException(InvalidRelease);
        }
        var bytes = RequestCqrsProbeJsonWriter.Release(sessionId, armId, requestId);
        var decoded = json.ReadRelease(bytes);
        if (decoded.SessionId != sessionId || decoded.ArmId != armId || decoded.RequestId != requestId)
        {
            throw new InvalidOperationException(InvalidRelease);
        }
        RequestCqrsProbeBroadcast.WriteIdentical(nodes, nodeDirectories, ownerRecords,
            RequestCqrsProbeFileNames.Release(armId, requestId), bytes);
        arm.ReleaseWritten = true;
    }

    internal static void RecordMarker(IDictionary<Guid, RequestCqrsProbeArmState> arms,
        string sessionId, RequestCqrsProbeJson json, IDictionary<string, int> activeGates,
        RequestCqrsProbeMarkerRecord marker, string node, string expectedPhase, string expectedOutcome,
        IReadOnlyList<ReplicaSiloDiscovery> signedDiscovery)
    {
        if (!arms.TryGetValue(marker.ArmId, out var arm))
        {
            throw new InvalidOperationException(MarkerMismatch);
        }
        RequestCqrsProbeMarkerState.Record(sessionId, arm, marker, node, expectedPhase, expectedOutcome,
            signedDiscovery, activeGates, json);
    }

    internal static async Task<RequestCqrsProbeMarkerRecord> WaitForMarkerAsync(
        RequestCqrsProbeFixture fixture, System.Threading.Lock sync,
        IDictionary<Guid, RequestCqrsProbeArmState> arms, Guid armId,
        RequestCqrsProbePhase phase, RequestCqrsProbeOutcome outcome,
        IReadOnlyList<ReplicaSiloDiscovery> signedDiscovery, CancellationToken cancellationToken)
    {
        var marker = await RequestCqrsProbeMarkerReader.WaitForMarkerAsync(fixture, armId, phase, outcome,
            signedDiscovery, cancellationToken).ConfigureAwait(false);
        lock (sync)
        {
            var arm = arms[armId];
            arm.RequestId ??= marker.RequestId;
            if (arm.RequestId != marker.RequestId)
            {
                throw new InvalidOperationException(MarkerMismatch);
            }
        }
        return marker;
    }
}
