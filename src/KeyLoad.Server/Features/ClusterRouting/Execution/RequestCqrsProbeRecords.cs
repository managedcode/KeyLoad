using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class RequestCqrsProbeRecords(string sessionId, string voter, byte[] ownerBytes,
    IOptions<RequestProbeExecutionOptions> executionOptions, RequestCqrsProbeJson json)
{
    private readonly Dictionary<Guid, (byte[] Bytes, RequestCqrsProbeArmRecord Record)> knownArms = [];
    private readonly HashSet<Guid> retiredArms = [];
    private readonly Dictionary<string, byte[]> observedControls = new(StringComparer.Ordinal);

    internal void ReadControl(string path, string name, List<RequestCqrsProbeLoadedArm> arms,
        List<RequestCqrsProbeReleaseRecord> releases, List<RequestCqrsProbeMarkerRecord> markers,
        HashSet<string> presentControls)
    {
        if (name == RequestCqrsProbeProtocol.OwnerFile)
        {
            if (!CryptographicOperations.FixedTimeEquals(ownerBytes, RequestCqrsProbeFiles.ReadRecord(path, executionOptions)))
            { throw Invalid(); }
            return;
        }

        if (name.StartsWith(RequestCqrsProbeProtocol.TemporaryFilePrefix, StringComparison.Ordinal))
        {
            // Bounded staging is invisible until the final atomic rename.
            return;
        }

        var bytes = RequestCqrsProbeFiles.ReadRecord(path, executionOptions);
        if (name.StartsWith(RequestCqrsProbeProtocol.ArmFilePrefix, StringComparison.Ordinal))
        {
            var arm = json.ReadArm(bytes);
            if (arm.SessionId != sessionId || name != RequestCqrsProbeFiles.ArmName(arm))
            { throw Invalid(); }
            arms.Add(new RequestCqrsProbeLoadedArm(arm, bytes));
            return;
        }
        if (name.StartsWith(RequestCqrsProbeProtocol.ReleaseFilePrefix, StringComparison.Ordinal))
        {
            var release = json.ReadRelease(bytes);
            if (release.SessionId != sessionId || name != RequestCqrsProbeFiles.ReleaseName(release))
            { throw Invalid(); }
            RegisterImmutable(name, bytes);
            presentControls.Add(name);
            releases.Add(release);
            return;
        }
        if (name.StartsWith(RequestCqrsProbeProtocol.MarkerFilePrefix, StringComparison.Ordinal))
        {
            var marker = json.ReadMarker(bytes);
            if (marker.SessionId != sessionId || marker.Voter != voter || name != RequestCqrsProbeFiles.MarkerName(marker))
            { throw Invalid(); }
            RegisterImmutable(name, bytes);
            presentControls.Add(name);
            markers.Add(marker);
            return;
        }
    }

    internal void ValidatePresence(HashSet<string> presentControls)
    {
        if (observedControls.Keys.Any(name => !presentControls.Contains(name)))
        { throw Invalid(); }
    }

    internal void ValidateInventory(IReadOnlyList<RequestCqrsProbeMarkerRecord> markers)
    {
        var retainedIdCount = knownArms.Keys.Concat(retiredArms).Distinct().Count();
        if (retainedIdCount > executionOptions.Value.MaximumArms
            || markers.GroupBy(marker => (marker.ArmId, marker.RequestId))
                .Any(group => group.Count() > executionOptions.Value.MaximumMarkersPerRequest))
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
        if (retainedIds > executionOptions.Value.MaximumArms)
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
        const int DistinctCountValidationBoundary = 1;

        foreach (var arm in arms)
        {
            RequestCqrsReceiverIssueAdjunct.RequireDeclaredPair(arm.Record, arms, markers);
            RequestCqrsSampleChunkProbePair.RequireDeclaredPair(arm.Record, arms);
            RequestCqrsEventVectorProbePair.RequireDeclaredPair(arm.Record, arms);
        }
        var armMap = knownArms.ToDictionary(pair => pair.Key, pair => pair.Value.Record);
        foreach (var arm in arms)
        { armMap[arm.Record.ArmId] = arm.Record; }
        if (markers.GroupBy(marker => marker.ArmId).Any(group => group.Select(marker => marker.RequestId).Distinct().Count() > DistinctCountValidationBoundary))
        { throw Invalid(); }
        foreach (var marker in markers)
        {
            if (armMap.TryGetValue(marker.ArmId, out var arm)
                && (arm.CommandId != marker.CommandId || marker.Phase != arm.Phase && marker.Phase != RequestCqrsProbePhase.ProducerDisposed
                    && !RequestCqrsProbeMarkerRules.CanonicalAdjunct(arm, marker)
                    || !RequestCqrsProbeMarkerRules.ValidOutcome(arm, marker)
                    || !RequestCqrsCanonicalScopeValidation.Matches(arm, marker)))
            { throw Invalid(); }
        }
        if (releases.GroupBy(release => release.ArmId)
            .Any(group => group.Select(release => release.RequestId).Distinct().Count() > DistinctCountValidationBoundary))
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
        => RequestCqrsProbeMarkerRules.Require(marker, snapshot, producerClaim,
            sessionId, voter, executionOptions, this);

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

    internal RequestCqrsProbeArmRecord? RetainedArm(Guid armId)
        => knownArms.TryGetValue(armId, out var known) ? known.Record : null;

    internal bool IsRetiredArm(Guid armId, byte[] bytes)
        => retiredArms.Contains(armId) && knownArms.TryGetValue(armId, out var known)
            && CryptographicOperations.FixedTimeEquals(known.Bytes, bytes);

    internal RequestCqrsProbeCleanupInventory CreateCleanupInventory()
        => new(knownArms, retiredArms, observedControls, executionOptions, json);

    private static InvalidOperationException Invalid() => new(RequestCqrsProbeProtocol.InvalidFiles);
}
