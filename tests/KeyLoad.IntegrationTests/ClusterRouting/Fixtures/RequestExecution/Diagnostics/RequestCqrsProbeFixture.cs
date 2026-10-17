using KeyLoad.Clustering;
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
    private readonly bool includeDestination;
    private bool admissionStopped;
    private bool cleaned;
    private bool retainEvidence;

    internal RequestCqrsProbeFixture(string root, string sessionId,
        Dictionary<string, string> directories, Dictionary<string, byte[]> owners, RequestCqrsProbeJson json,
        bool includeDestination = false)
    {
        this.includeDestination = includeDestination;
        Root = root;
        SessionId = sessionId;
        Json = json;
        nodeDirectories = directories;
        ownerRecords = owners;
        foreach (var node in Nodes)
        {
            activeGates.Add(node, 0);
        }
    }

    internal string Root
    {
        get;
    }
    internal string SessionId
    {
        get;
    }
    internal RequestCqrsProbeJson Json
    {
        get;
    }

    internal static RequestCqrsProbeFixture Create(string dataRoot, Guid sessionId)
        => RequestCqrsProbeFixtureFactory.Create(dataRoot, sessionId);

    internal static RequestCqrsProbeFixture CreateTwoRf3(string dataRoot, Guid sessionId)
        => RequestCqrsProbeFixtureFactory.Create(dataRoot, sessionId, includeDestination: true);

    internal Guid WriteArm(string principalId, Guid commandId, GrainReadKind? readKind,
        RequestCqrsProbePhase phase, RequestCqrsProbeAction action, PartitionRef? partition = null, Guid? sourceRequestId = null, string? targetVoter = null, Guid? sourceArmId = null)
    {
        lock (sync)
        {
            return RequestCqrsProbeArmAdmission.WriteArm(admissionStopped, cleaned, arms, SessionId, Json, Nodes,
                nodeDirectories, ownerRecords, principalId, commandId, readKind, phase, action,
                partition, sourceRequestId, targetVoter, sourceArmId);
        }
    }

    internal void WriteReplayPlan(Guid armId, KeyLoad.Server.Features.DocumentStorage.RemoteModelNativeReplayVariation mode)
    {
        lock (sync)
        {
            RequestCqrsProbeArmAdmission.WriteReplayPlan(admissionStopped, cleaned, arms, SessionId, Json, Nodes,
                nodeDirectories, ownerRecords, armId, mode);
        }
    }

    internal void WriteRelease(Guid armId, Guid requestId)
    {
        lock (sync)
        {
            RequestCqrsProbeArmAdmission.WriteRelease(arms, SessionId, Json, Nodes, nodeDirectories,
                ownerRecords, armId, requestId);
        }
    }

    internal void StopAdmission()
    {
        lock (sync)
        {
            admissionStopped = true;
        }
    }

    internal async Task ReleaseOpenArmsAsync(IReadOnlyList<ReplicaSiloDiscovery> signedDiscovery,
        CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled)
        {
            throw new InvalidOperationException(MissingScenarioDeadline);
        }
        RequestCqrsProbeArmState[] open;
        lock (sync)
        {
            admissionStopped = true;
            open = arms.Values.Where(arm => arm.Action == RequestCqrsProbeAction.Hold
                && arm.RequestId is not null && !arm.Settled && !arm.DisposedGateJoined && !arm.ProcessOwnerJoined).ToArray();
        }
        foreach (var arm in open)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!arm.ReleaseWritten)
            {
                WriteRelease(arm.ArmId, arm.RequestId!.Value);
            }
            await RequestCqrsProbeMarkerReader.WaitForSettlementAsync(this, arm.ArmId, signedDiscovery,
                cancellationToken).ConfigureAwait(false);
        }
    }

    internal Task<RequestCqrsProbeMarkerRecord> WaitForMarkerAsync(Guid armId,
        RequestCqrsProbePhase phase, RequestCqrsProbeOutcome outcome,
        IReadOnlyList<ReplicaSiloDiscovery> signedDiscovery, CancellationToken cancellationToken)
        => RequestCqrsProbeArmAdmission.WaitForMarkerAsync(this, sync, arms, armId, phase, outcome,
            signedDiscovery, cancellationToken);

    internal Task WaitForSettlementAsync(Guid armId, IReadOnlyList<ReplicaSiloDiscovery> signedDiscovery,
        CancellationToken cancellationToken)
        => RequestCqrsProbeMarkerReader.WaitForSettlementAsync(this, armId, signedDiscovery, cancellationToken);

    internal Task RetireArmAsync(Guid armId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            if (cleaned || !arms.TryGetValue(armId, out var arm) || arm.Retired
                || (arm.Action == RequestCqrsProbeAction.Hold && arm.RequestId is not null && !arm.Settled && !arm.DisposedGateJoined && !arm.ProcessOwnerJoined))
            {
                throw new InvalidOperationException(InvalidArm);
            }
            RequestCqrsProbeFixtureOperations.RetireArmFiles(armId, arm.Bytes, nodeDirectories, ownerRecords);
            arm.Retired = true;
        }
        return Task.CompletedTask;
    }

    internal void JoinDisposedGate(Guid armId, Task originalProducer)
    {
        lock (sync)
        {
            RequestCqrsProbeDisposedGateCleanup.Join(ArmFor(armId), activeGates, originalProducer);
        }
    }

    internal void JoinKilledOwnerGates(TwoRf3MembershipWave wave, Aspire.Hosting.DistributedApplication originalOwner,
        IReadOnlyList<Guid> originalArms, KeyLoad.Server.Features.ClusterRouting.RequestCqrsMovementJournalRecord? movementJournal = null,
        RequestCqrsProbeMarkerRecord? originalMarker = null, Task? originalProducer = null)
    {
        lock (sync)
        {
            RequestCqrsProbeKilledOwnerCleanup.Join(wave, originalOwner, originalArms, arms, activeGates, Json, movementJournal, originalMarker, originalProducer);
        }
    }

    internal void RetainEvidence()
    {
        lock (sync)
        {
            retainEvidence = true;
        }
    }

    internal Task DisposeAfterResourcesJoinedAsync()
    {
        lock (sync)
        {
            admissionStopped = true;
            if (arms.Values.Any(arm => (arm.Action == RequestCqrsProbeAction.Hold
                    && arm.RequestId is not null && !arm.Settled && !arm.DisposedGateJoined && !arm.ProcessOwnerJoined && !arm.Retired)
                || (arm.RequestId is not null && !arm.OwnerDisposedSeen && !arm.ProcessOwnerJoined)))
            {
                throw new InvalidOperationException(UnsettledGates);
            }
            if (cleaned || retainEvidence)
            {
                return Task.CompletedTask;
            }
        }
        RequestCqrsProbeFileStore.DeleteOwnedTree(Root, nodeDirectories, ownerRecords, requireAllOwners: true, includeDestination);
        lock (sync)
        {
            cleaned = true;
        }
        return Task.CompletedTask;
    }

    internal Task VerifyDestinationOwnerOnlyAsync(CancellationToken token)
        => RequestCqrsProbeFixtureOperations.VerifyDestinationOwnerOnlyAsync(this, includeDestination, token);

    internal (string Directory, byte[] OwnerBytes) NodeFor(string node)
        => (nodeDirectories[node], ownerRecords[node]);

    internal RequestCqrsProbeArmState ArmFor(Guid armId)
    {
        lock (sync)
        {
            return arms.TryGetValue(armId, out var state) ? state : throw new InvalidOperationException(InvalidArm);
        }
    }

    internal void RecordMarker(RequestCqrsProbeMarkerRecord marker, string node, string expectedPhase,
        string expectedOutcome, IReadOnlyList<ReplicaSiloDiscovery> signedDiscovery)
    {
        lock (sync)
        {
            RequestCqrsProbeArmAdmission.RecordMarker(arms, SessionId, Json, activeGates, marker, node,
                expectedPhase, expectedOutcome, signedDiscovery);
        }
    }

}
