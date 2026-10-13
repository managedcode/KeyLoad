using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class RequestCqrsMovementJournalInventory
{
    internal const string Kind = "request-movement-journal";
    private const string Prefix = "movement-journal-";
    private const string Suffix = ".json";
    private const int GuidHexCharacters = 32;
    private const int IdentifierPairCharacters = 65;
    private const int RequestOffset = 33;
    private const int ExactWitnessCount = 1;
    private const int DigestHexCharacters = 64;
    private const string IdentifierFormat = "N";
    private const string IdentifierSeparator = "-";
    private const char IdentifierSeparatorCharacter = '-';
    private const long UnappliedJournalPosition = 0;
    private const long UnassignedOwnerEpoch = 0;
    private const int OriginalMarkerIndex = 0;
    private static readonly string[] FieldNames =
    [
        nameof(RequestCqrsMovementJournalRecord.Version),
        nameof(RequestCqrsMovementJournalRecord.Kind),
        nameof(RequestCqrsMovementJournalRecord.SessionId),
        nameof(RequestCqrsMovementJournalRecord.ArmId),
        nameof(RequestCqrsMovementJournalRecord.RequestId),
        nameof(RequestCqrsMovementJournalRecord.CommandId),
        nameof(RequestCqrsMovementJournalRecord.ConnectionId),
        nameof(RequestCqrsMovementJournalRecord.PrincipalId),
        nameof(RequestCqrsMovementJournalRecord.Voter),
        nameof(RequestCqrsMovementJournalRecord.SiloAddress),
        nameof(RequestCqrsMovementJournalRecord.Partition),
        nameof(RequestCqrsMovementJournalRecord.MoveId),
        nameof(RequestCqrsMovementJournalRecord.Stage),
        nameof(RequestCqrsMovementJournalRecord.Journal),
        nameof(RequestCqrsMovementJournalRecord.Error),
    ];
    internal static ReadOnlySpan<string> Fields => FieldNames;
    internal static string Name(Guid arm, Guid request)
        => string.Concat(Prefix, arm.ToString(IdentifierFormat), IdentifierSeparator, request.ToString(IdentifierFormat), Suffix);

    internal static bool IsName(string name)
    {
        if (name.Length != Prefix.Length + IdentifierPairCharacters + Suffix.Length
            || !name.StartsWith(Prefix, StringComparison.Ordinal) || !name.EndsWith(Suffix, StringComparison.Ordinal))
        { return false; }
        var ids = name.AsSpan(Prefix.Length, IdentifierPairCharacters);
        return ids[GuidHexCharacters] == IdentifierSeparatorCharacter && Guid.TryParseExact(ids[..GuidHexCharacters], IdentifierFormat, out var arm)
            && Guid.TryParseExact(ids[RequestOffset..], IdentifierFormat, out var request)
            && arm != Guid.Empty && request != Guid.Empty && Name(arm, request) == name;
    }

    internal static void Require(RequestCqrsMovementJournalRecord value)
    {
        if (value.Version != RequestCqrsProbeProtocol.Version || value.Kind != Kind
            || !RequestCqrsProbeOptionsReader.IsSessionId(value.SessionId) || value.ArmId == Guid.Empty
            || value.RequestId == Guid.Empty || value.CommandId == Guid.Empty || value.ConnectionId == Guid.Empty
            || string.IsNullOrWhiteSpace(value.PrincipalId) || string.IsNullOrWhiteSpace(value.Voter)
            || string.IsNullOrWhiteSpace(value.SiloAddress) || value.Partition is null)
        { throw Invalid(); }
        if (value.Error is { } error)
        {
            if (!Enum.IsDefined(error) || value.MoveId != Guid.Empty || value.Stage is not null || value.Journal is not null)
            { throw Invalid(); }
            return;
        }
        if (value.MoveId == Guid.Empty || !IsObservedControlStage(value.Stage)
            || value.Journal is not { } journal || journal.CommandId != value.CommandId
            || journal.AppliedPosition <= UnappliedJournalPosition || !Digest(journal.ControlIntentDigest)
            || journal.EffectDigest is { } effect && !Digest(effect)
            || journal.PhysicalOwner is not { } owner || owner.PhysicalShardId == Guid.Empty
            || owner.Incarnation == Guid.Empty || owner.PlacementEpoch <= UnassignedOwnerEpoch || owner.VoterIds.IsDefaultOrEmpty
            || owner.VoterIds.Any(string.IsNullOrWhiteSpace)
            || owner.VoterIds.Distinct(StringComparer.Ordinal).Count() != owner.VoterIds.Length
            || !owner.VoterIds.Contains(value.Voter, StringComparer.Ordinal))
        { throw Invalid(); }
    }

    internal static void RequireArm(RequestCqrsMovementJournalRecord value, RequestCqrsProbeArmRecord arm)
    {
        if (arm.SessionId != value.SessionId || arm.ArmId != value.ArmId || arm.CommandId != value.CommandId
            || arm.PrincipalId != value.PrincipalId || arm.Partition?.ToPartition() != value.Partition
            || arm.ReadKind is not null || arm.Phase != RequestCqrsProbePhase.SubmitReturned
            || arm.Action != RequestCqrsProbeAction.Hold || arm.TargetVoter is not null
            || arm.SourceRequestId is not null || arm.SourceArmId is not null)
        { throw Invalid(); }
    }

    internal static void RequireMarker(RequestCqrsMovementJournalRecord value, RequestCqrsProbeMarkerRecord marker)
    {
        if (marker.SessionId != value.SessionId || marker.ArmId != value.ArmId || marker.RequestId != value.RequestId
            || marker.CommandId != value.CommandId || marker.Voter != value.Voter || marker.SiloAddress != value.SiloAddress
            || marker.Phase != RequestCqrsProbePhase.SubmitReturned || marker.Outcome != RequestCqrsProbeOutcome.Observed
            || marker.EntryIndex is not null || marker.EntryTerm is not null)
        { throw Invalid(); }
    }

    internal static void RequireInventory(IEnumerable<RequestCqrsMovementJournalRecord> values,
        IReadOnlyList<RequestCqrsProbeMarkerRecord> markers, IReadOnlyList<RequestCqrsProbeLoadedArm> arms,
        Func<Guid, RequestCqrsProbeArmRecord?> retainedArm)
    {
        var witnesses = values.ToArray();
        foreach (var value in witnesses)
        {
            Require(value);
            var originals = markers.Where(marker => marker.SessionId == value.SessionId && marker.ArmId == value.ArmId
                && marker.RequestId == value.RequestId && marker.Phase == RequestCqrsProbePhase.SubmitReturned
                && marker.Outcome == RequestCqrsProbeOutcome.Observed).ToArray();
            if (originals.Length != ExactWitnessCount)
            { throw Invalid(); }
            RequireMarker(value, originals[OriginalMarkerIndex]);
        }
        RequireScopedMarkers(witnesses, markers, arms, retainedArm);
    }

    private static void RequireScopedMarkers(IReadOnlyList<RequestCqrsMovementJournalRecord> witnesses,
        IReadOnlyList<RequestCqrsProbeMarkerRecord> markers, IReadOnlyList<RequestCqrsProbeLoadedArm> arms,
        Func<Guid, RequestCqrsProbeArmRecord?> retainedArm)
    {
        foreach (var marker in markers.Where(value => value.Phase == RequestCqrsProbePhase.SubmitReturned
            && value.Outcome == RequestCqrsProbeOutcome.Observed))
        {
            var arm = arms.SingleOrDefault(value => value.Record.ArmId == marker.ArmId)?.Record
                ?? retainedArm(marker.ArmId);
            if (arm?.Partition is null)
            { continue; }
            if (witnesses.Count(value => value.ArmId == marker.ArmId && value.RequestId == marker.RequestId)
                != ExactWitnessCount)
            { throw Invalid(); }
        }
    }

    internal static bool IsObservedControlStage(PartitionMovePeerStage? stage)
        => stage is PartitionMovePeerStage.ControlPrepare or PartitionMovePeerStage.ControlAcceptFence
            or PartitionMovePeerStage.ControlFinalize;

    private static bool Digest(string value) => value is { Length: DigestHexCharacters } && value.All(Uri.IsHexDigit);
    private static InvalidOperationException Invalid() => new(RequestCqrsProbeProtocol.InvalidRecord);
}
