using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;
using static KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsProbeFixtureProtocol;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Owns one bounded private control session, not the AppHost or its database roots.</summary>
internal sealed class RequestCqrsProbeFixture
{
    private readonly System.Threading.Lock sync = new();
    private readonly Dictionary<string, string> nodeDirectories = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> ownerRecords = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, RequestCqrsProbeArmState> arms = [];
    private readonly Dictionary<string, int> activeGates = new(StringComparer.Ordinal);
    private bool admissionStopped;
    private bool cleaned;
    private bool retainEvidence;

    internal RequestCqrsProbeFixture(string root, string sessionId,
        Dictionary<string, string> directories, Dictionary<string, byte[]> owners, RequestCqrsProbeJson json)
    {
        Root = root;
        SessionId = sessionId;
        Json = json;
        nodeDirectories = directories;
        ownerRecords = owners;
        foreach (var node in Nodes)
        { activeGates.Add(node, 0); }
    }

    internal string Root { get; }
    internal string SessionId { get; }
    internal RequestCqrsProbeJson Json { get; }

    internal static RequestCqrsProbeFixture Create(string dataRoot, Guid sessionId)
        => RequestCqrsProbeFixtureFactory.Create(dataRoot, sessionId);

    internal Guid WriteArm(string principalId, Guid commandId, GrainReadKind? readKind,
        RequestCqrsProbePhase phase, RequestCqrsProbeAction action, PartitionRef? partition = null, Guid? sourceRequestId = null, string? targetVoter = null, Guid? sourceArmId = null)
    {
        lock (sync)
        {
            if (admissionStopped || cleaned)
            { throw new InvalidOperationException(AdmissionStopped); }
            RequestCqrsProbeArmValidator.Validate(principalId, commandId, readKind, phase, action);
            if (arms.Count >= MaximumArms)
            { throw new IOException(FileLimitExceeded); }
            var armId = Guid.NewGuid();
            while (arms.ContainsKey(armId))
            { armId = Guid.NewGuid(); }
            var bytes = RequestCqrsProbeJsonWriter.Arm(SessionId, armId, principalId, commandId, readKind, phase, action, partition, sourceRequestId, targetVoter, sourceArmId);
            var decoded = Json.ReadArm(bytes);
            RequestCqrsProbeArmValidator.ValidateDecoded(decoded, armId, principalId, commandId, readKind, phase, action);
            if (decoded.Partition?.ToPartition() != partition || decoded.SourceRequestId != sourceRequestId || decoded.TargetVoter != targetVoter || decoded.SourceArmId != sourceArmId)
            { throw new InvalidOperationException(InvalidArm); }
            RequestCqrsProbeBroadcast.WriteIdentical(Nodes, nodeDirectories, ownerRecords,
                RequestCqrsProbeFileNames.Arm(armId), bytes);
            var state = new RequestCqrsProbeArmState(principalId, commandId, readKind, phase, action, bytes)
            { ArmId = armId };
            arms.Add(armId, state);
            return armId;
        }
    }

    internal void WriteRelease(Guid armId, Guid requestId)
    {
        lock (sync)
        {
            if (!arms.TryGetValue(armId, out var arm) || arm.Action != RequestCqrsProbeAction.Hold
                || arm.Retired || arm.Settled || arm.DisposedGateJoined || requestId == Guid.Empty
                || arm.RequestId != requestId || arm.ReleaseWritten)
            { throw new InvalidOperationException(InvalidRelease); }
            var bytes = RequestCqrsProbeJsonWriter.Release(SessionId, armId, requestId);
            var decoded = Json.ReadRelease(bytes);
            if (decoded.SessionId != SessionId || decoded.ArmId != armId || decoded.RequestId != requestId)
            { throw new InvalidOperationException(InvalidRelease); }
            RequestCqrsProbeBroadcast.WriteIdentical(Nodes, nodeDirectories, ownerRecords,
                RequestCqrsProbeFileNames.Release(armId, requestId), bytes);
            arm.ReleaseWritten = true;
        }
    }

    internal void StopAdmission()
    {
        lock (sync)
        { admissionStopped = true; }
    }

    internal async Task ReleaseOpenArmsAsync(IReadOnlyList<ReplicaSiloDiscovery> signedDiscovery,
        CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled)
        { throw new InvalidOperationException(MissingScenarioDeadline); }
        RequestCqrsProbeArmState[] open;
        lock (sync)
        {
            admissionStopped = true;
            open = arms.Values.Where(arm => arm.Action == RequestCqrsProbeAction.Hold
                && arm.RequestId is not null && !arm.Settled && !arm.DisposedGateJoined).ToArray();
        }
        foreach (var arm in open)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!arm.ReleaseWritten)
            { WriteRelease(arm.ArmId, arm.RequestId!.Value); }
            await RequestCqrsProbeMarkerReader.WaitForSettlementAsync(this, arm.ArmId, signedDiscovery,
                cancellationToken).ConfigureAwait(false);
        }
    }

    internal async Task<RequestCqrsProbeMarkerRecord> WaitForMarkerAsync(Guid armId,
        RequestCqrsProbePhase phase, RequestCqrsProbeOutcome outcome,
        IReadOnlyList<ReplicaSiloDiscovery> signedDiscovery, CancellationToken cancellationToken)
    {
        var marker = await RequestCqrsProbeMarkerReader.WaitForMarkerAsync(this, armId, phase, outcome,
            signedDiscovery, cancellationToken).ConfigureAwait(false);
        lock (sync)
        {
            var arm = arms[armId];
            arm.RequestId ??= marker.RequestId;
            if (arm.RequestId != marker.RequestId)
            { throw new InvalidOperationException(MarkerMismatch); }
        }
        return marker;
    }

    internal Task WaitForSettlementAsync(Guid armId, IReadOnlyList<ReplicaSiloDiscovery> signedDiscovery,
        CancellationToken cancellationToken)
        => RequestCqrsProbeMarkerReader.WaitForSettlementAsync(this, armId, signedDiscovery, cancellationToken);

    internal Task RetireArmAsync(Guid armId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            if (cleaned || !arms.TryGetValue(armId, out var arm) || arm.Retired
                || (arm.Action == RequestCqrsProbeAction.Hold && arm.RequestId is not null && !arm.Settled && !arm.DisposedGateJoined))
            { throw new InvalidOperationException(InvalidArm); }
            foreach (var voter in Nodes)
            {
                var directory = nodeDirectories[voter];
                RequestCqrsProbeFileStore.VerifyOwnerFile(directory, ownerRecords[voter]);
                RequestCqrsProbeFileStore.VerifyExactFile(directory, RequestCqrsProbeFileNames.Arm(armId), arm.Bytes);
            }
            foreach (var voter in Nodes)
            { RequestCqrsProbeFileStore.DeleteExactFile(nodeDirectories[voter], RequestCqrsProbeFileNames.Arm(armId), arm.Bytes); }
            arm.Retired = true;
        }
        return Task.CompletedTask;
    }

    internal void JoinDisposedGate(Guid armId, Task originalProducer)
    { lock (sync) { RequestCqrsProbeDisposedGateCleanup.Join(ArmFor(armId), activeGates, originalProducer); } }

    internal void RetainEvidence()
    {
        lock (sync)
        { retainEvidence = true; }
    }

    internal Task DisposeAfterResourcesJoinedAsync()
    {
        lock (sync)
        {
            admissionStopped = true;
            if (arms.Values.Any(arm => (arm.Action == RequestCqrsProbeAction.Hold
                    && arm.RequestId is not null && !arm.Settled && !arm.DisposedGateJoined && !arm.Retired)
                || (arm.RequestId is not null && !arm.OwnerDisposedSeen)))
            { throw new InvalidOperationException(UnsettledGates); }
            if (cleaned || retainEvidence)
            { return Task.CompletedTask; }
        }
        RequestCqrsProbeFileStore.DeleteOwnedTree(Root, nodeDirectories, ownerRecords, requireAllOwners: true);
        lock (sync)
        { cleaned = true; }
        return Task.CompletedTask;
    }

    internal (string Directory, byte[] OwnerBytes) NodeFor(string node)
        => (nodeDirectories[node], ownerRecords[node]);

    internal RequestCqrsProbeArmState ArmFor(Guid armId)
    {
        lock (sync)
        { return arms.TryGetValue(armId, out var state) ? state : throw new InvalidOperationException(InvalidArm); }
    }

    internal void RecordMarker(RequestCqrsProbeMarkerRecord marker, string node, string expectedPhase,
        string expectedOutcome, IReadOnlyList<ReplicaSiloDiscovery> signedDiscovery)
    {
        lock (sync)
        {
            if (!arms.TryGetValue(marker.ArmId, out var arm))
            { throw new InvalidOperationException(MarkerMismatch); }
            RequestCqrsProbeMarkerState.Record(SessionId, arm, marker, node, expectedPhase, expectedOutcome,
                signedDiscovery, activeGates, Json);
        }
    }

}
