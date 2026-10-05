using System.Security.Cryptography;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class RequestCqrsProbeRecords(string sessionId, string voter, byte[] ownerBytes,
    RequestCqrsProbeDiscoveryPolicy discoveryPolicy)
{
    private readonly Dictionary<Guid, (byte[] Bytes, RequestCqrsProbeArmRecord Record)> knownArms = [];
    private readonly HashSet<Guid> retiredArms = [];
    private readonly Dictionary<string, byte[]> observedControls = new(StringComparer.Ordinal);
    private readonly List<RequestCqrsProbeDiscoveryRecord> discoveries = [];

    internal void ReadControl(string path, string name, List<RequestCqrsProbeLoadedArm> arms,
        List<RequestCqrsProbeReleaseRecord> releases, List<RequestCqrsProbeMarkerRecord> markers,
        HashSet<string> presentControls, List<RequestCqrsProbeDiscoveryRecord> loadedDiscoveries)
    {
        if (name == RequestCqrsProbeProtocol.OwnerFile)
        {
            if (!CryptographicOperations.FixedTimeEquals(ownerBytes, RequestCqrsProbeFiles.ReadRecord(path)))
            { throw Invalid(); }
            return;
        }

        if (name.StartsWith("tmp-", StringComparison.Ordinal))
        {
            // Bounded staging is invisible until the final atomic rename.
            return;
        }

        var bytes = RequestCqrsProbeFiles.ReadRecord(path);
        if (name.StartsWith("arm-", StringComparison.Ordinal))
        {
            var arm = RequestCqrsProbeJson.ReadArm(bytes);
            if (arm.SessionId != sessionId || name != RequestCqrsProbeFiles.ArmName(arm))
            { throw Invalid(); }
            arms.Add(new RequestCqrsProbeLoadedArm(arm, bytes));
            return;
        }
        if (name.StartsWith("release-", StringComparison.Ordinal))
        {
            var release = RequestCqrsProbeJson.ReadRelease(bytes);
            if (release.SessionId != sessionId || name != RequestCqrsProbeFiles.ReleaseName(release))
            { throw Invalid(); }
            RegisterImmutable(name, bytes);
            presentControls.Add(name);
            releases.Add(release);
            return;
        }
        if (name.StartsWith("marker-", StringComparison.Ordinal))
        {
            var marker = RequestCqrsProbeJson.ReadMarker(bytes);
            if (marker.SessionId != sessionId || marker.Voter != voter || name != RequestCqrsProbeFiles.MarkerName(marker))
            { throw Invalid(); }
            RegisterImmutable(name, bytes);
            presentControls.Add(name);
            markers.Add(marker);
            return;
        }
        if (name.StartsWith(RequestCqrsProbeProtocol.DiscoveryFilePrefix, StringComparison.Ordinal))
        {
            ReadDiscovery(name, bytes, presentControls, loadedDiscoveries);
        }
    }

    private void ReadDiscovery(string name, byte[] bytes, HashSet<string> presentControls,
        List<RequestCqrsProbeDiscoveryRecord> loadedDiscoveries)
    {
            var discovery = RequestCqrsProbeJson.ReadDiscovery(bytes);
            discoveryPolicy.Validate(sessionId, voter, discovery);
            var slot = discoveryPolicy.GetSlot(discovery.PeerVoterId);
            if (name != RequestCqrsProbeFiles.DiscoveryName(slot))
            { throw Invalid(); }
            RegisterImmutable(name, bytes);
            presentControls.Add(name);
            loadedDiscoveries.Add(discovery);
    }

    internal void ValidatePresence(HashSet<string> presentControls)
    {
        if (observedControls.Keys.Any(name => !presentControls.Contains(name)))
        { throw Invalid(); }
    }

    internal void ValidateDiscoveryInventory(IReadOnlyList<RequestCqrsProbeDiscoveryRecord> loaded)
    {
        RequestCqrsProbeDiscoveryPolicy.ValidateInventory(loaded);
        discoveries.Clear();
        discoveries.AddRange(loaded);
    }

    internal int GetDiscoverySlot(string peer) => discoveryPolicy.GetSlot(peer);

    internal void ValidateDiscoveryForWrite(RequestCqrsProbeDiscoveryRecord record)
    {
        discoveryPolicy.Validate(sessionId, voter, record);
        RequestCqrsProbeDiscoveryPolicy.ValidateWrite(discoveries, record);
    }

    internal void ValidateInventory(IReadOnlyList<RequestCqrsProbeMarkerRecord> markers)
    {
        var retainedIdCount = knownArms.Keys.Concat(retiredArms).Distinct().Count();
        if (retainedIdCount > RequestCqrsProbeProtocol.MaximumArms
            || markers.GroupBy(marker => (marker.ArmId, marker.RequestId))
                .Any(group => group.Count() > RequestCqrsProbeProtocol.MaximumMarkersPerRequest))
        { throw Invalid(); }
    }

    internal void CommitArmInventory(IReadOnlyList<RequestCqrsProbeLoadedArm> arms,
        IReadOnlyList<RequestCqrsProbeReleaseRecord> releases, IReadOnlyList<RequestCqrsProbeMarkerRecord> markers)
    {
        var active = arms.Select(arm => arm.Record.ArmId).ToHashSet();
        if (active.Count != arms.Count || arms.Any(arm => retiredArms.Contains(arm.Record.ArmId)
            || knownArms.TryGetValue(arm.Record.ArmId, out var known)
                && !CryptographicOperations.FixedTimeEquals(known.Bytes, arm.ExactBytes)))
        { throw Invalid(); }
        var newlyRetired = knownArms.Keys.Concat(markers.Select(marker => marker.ArmId))
            .Concat(releases.Select(release => release.ArmId)).Where(armId => !active.Contains(armId)).Distinct().ToArray();
        var retainedIds = knownArms.Keys.Concat(retiredArms).Concat(newlyRetired).Concat(active).Distinct().Count();
        if (retainedIds > RequestCqrsProbeProtocol.MaximumArms)
        { throw Invalid(); }
        retiredArms.UnionWith(newlyRetired);
        foreach (var arm in arms)
        {
            if (!knownArms.ContainsKey(arm.Record.ArmId))
            { knownArms.Add(arm.Record.ArmId, (arm.ExactBytes.ToArray(), arm.Record)); }
        }
    }

    internal void ValidateCrossRecords(IReadOnlyList<RequestCqrsProbeLoadedArm> arms,
        IReadOnlyList<RequestCqrsProbeReleaseRecord> releases, IReadOnlyList<RequestCqrsProbeMarkerRecord> markers)
    {
        var armMap = knownArms.ToDictionary(pair => pair.Key, pair => pair.Value.Record);
        foreach (var arm in arms)
        { armMap[arm.Record.ArmId] = arm.Record; }
        if (markers.GroupBy(marker => marker.ArmId).Any(group => group.Select(marker => marker.RequestId).Distinct().Count() > 1))
        { throw Invalid(); }
        foreach (var marker in markers)
        {
            if (armMap.TryGetValue(marker.ArmId, out var arm)
                && (arm.CommandId != marker.CommandId || marker.Phase != arm.Phase && marker.Phase != RequestCqrsProbePhase.ProducerDisposed
                    || !ValidOutcome(arm, marker)))
            { throw Invalid(); }
        }
        if (releases.GroupBy(release => release.ArmId)
            .Any(group => group.Select(release => release.RequestId).Distinct().Count() > 1))
        { throw Invalid(); }
        foreach (var release in releases)
        {
            if (armMap.TryGetValue(release.ArmId, out var arm) && arm.Action != RequestCqrsProbeAction.Hold
                || markers.Any(marker => marker.ArmId == release.ArmId && marker.RequestId != release.RequestId))
            { throw Invalid(); }
        }
    }

    internal void ValidateMarker(RequestCqrsProbeMarkerRecord marker, RequestCqrsProbeSnapshot snapshot,
        RequestCqrsProbeLoadedArm? producerClaim)
    {
        if (marker.Version != RequestCqrsProbeProtocol.Version || marker.Kind != RequestCqrsProbeProtocol.MarkerKind
            || marker.SessionId != sessionId || marker.Voter != voter || marker.RequestId == Guid.Empty
            || marker.Phase is not (RequestCqrsProbePhase.ProducerDisposed
                or RequestCqrsProbePhase.RequestStarted or RequestCqrsProbePhase.AuthorizationReload
                or RequestCqrsProbePhase.BeforeSubmit or RequestCqrsProbePhase.SubmitReturned)
            || snapshot.Markers.Any(existing => RequestCqrsProbeFiles.MarkerName(existing) == RequestCqrsProbeFiles.MarkerName(marker)))
        { throw Invalid(); }
        var group = snapshot.Markers
            .Where(existing => existing.ArmId == marker.ArmId && existing.RequestId == marker.RequestId).ToArray();
        var active = snapshot.Arms.SingleOrDefault(arm => arm.Record.ArmId == marker.ArmId);
        var activeClaim = active is not null && producerClaim is not null
            && CryptographicOperations.FixedTimeEquals(active.ExactBytes, producerClaim.ExactBytes);
        var retiredProducerClaim = marker.Phase == RequestCqrsProbePhase.ProducerDisposed && producerClaim is not null
            && producerClaim.Record.ArmId == marker.ArmId && IsRetiredArm(marker.ArmId, producerClaim.ExactBytes);
        var validArm = marker.Phase == RequestCqrsProbePhase.ProducerDisposed
            ? activeClaim || retiredProducerClaim
            : producerClaim is null && active is not null;
        if (group.Length >= RequestCqrsProbeProtocol.MaximumMarkersPerRequest || !validArm)
        { throw Invalid(); }
    }

    internal void RegisterImmutable(string name, byte[] bytes)
    {
        if (observedControls.TryGetValue(name, out var existing))
        {
            if (!CryptographicOperations.FixedTimeEquals(existing, bytes))
            { throw Invalid(); }
            return;
        }
        observedControls.Add(name, bytes.ToArray());
    }

    internal bool IsRetiredArm(Guid armId, byte[] bytes)
        => retiredArms.Contains(armId) && knownArms.TryGetValue(armId, out var known)
            && CryptographicOperations.FixedTimeEquals(known.Bytes, bytes);

    private static bool ValidOutcome(RequestCqrsProbeArmRecord arm, RequestCqrsProbeMarkerRecord marker)
    {
        if (marker.Phase == RequestCqrsProbePhase.ProducerDisposed)
        { return marker.Outcome == RequestCqrsProbeOutcome.Observed; }
        return arm.Action == RequestCqrsProbeAction.Hold
            ? marker.Outcome is RequestCqrsProbeOutcome.Observed or RequestCqrsProbeOutcome.Released or RequestCqrsProbeOutcome.Cancelled
            : marker.Outcome == RequestCqrsProbeOutcome.FaultRequested;
    }

    private static InvalidOperationException Invalid() => new(RequestCqrsProbeProtocol.InvalidFiles);
}
