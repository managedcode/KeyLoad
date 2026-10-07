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

        var armMap = knownArms.ToDictionary(pair => pair.Key, pair => pair.Value.Record);
        foreach (var arm in arms)
        { armMap[arm.Record.ArmId] = arm.Record; }
        if (markers.GroupBy(marker => marker.ArmId).Any(group => group.Select(marker => marker.RequestId).Distinct().Count() > DistinctCountValidationBoundary))
        { throw Invalid(); }
        foreach (var marker in markers)
        {
            if (armMap.TryGetValue(marker.ArmId, out var arm)
                && (arm.CommandId != marker.CommandId || marker.Phase != arm.Phase && marker.Phase != RequestCqrsProbePhase.ProducerDisposed
                    && !CanonicalAdjunct(arm, marker)
                    || !ValidOutcome(arm, marker)
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
    {
        if (marker.Version != RequestCqrsProbeProtocol.Version || marker.Kind != RequestCqrsProbeProtocol.MarkerKind
            || marker.SessionId != sessionId || marker.Voter != voter || marker.RequestId == Guid.Empty
            || !RequestCqrsCanonicalMarkerValidation.Valid(marker)
            || marker.Phase is not (RequestCqrsProbePhase.ProducerDisposed
                or RequestCqrsProbePhase.RequestStarted or RequestCqrsProbePhase.AuthorizationReload
                or RequestCqrsProbePhase.BeforeSubmit or RequestCqrsProbePhase.SubmitReturned
                or RequestCqrsProbePhase.CanonicalJournalFlushed or RequestCqrsProbePhase.CanonicalOutboundObserved
                or RequestCqrsProbePhase.CanonicalIndependentAppendCompleted or RequestCqrsProbePhase.CanonicalOwnerDisposed)
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
            : active is not null && (producerClaim is null || activeClaim && CanonicalAdjunct(active.Record, marker));
        if (group.Any(existing => existing.EntryIndex != marker.EntryIndex || existing.EntryTerm != marker.EntryTerm)
            || active is not null && !RequestCqrsCanonicalScopeValidation.Matches(active.Record, marker)
            || group.Length >= executionOptions.Value.MaximumMarkersPerRequest || !validArm)
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

    private static bool CanonicalAdjunct(RequestCqrsProbeArmRecord arm, RequestCqrsProbeMarkerRecord marker)
        => arm.Phase == RequestCqrsProbePhase.CanonicalJournalFlushed && arm.Action == RequestCqrsProbeAction.Hold
            && arm.Partition is not null && marker.Outcome == RequestCqrsProbeOutcome.Observed
            && marker.Phase is RequestCqrsProbePhase.CanonicalOutboundObserved or RequestCqrsProbePhase.CanonicalIndependentAppendCompleted or RequestCqrsProbePhase.CanonicalOwnerDisposed;

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
