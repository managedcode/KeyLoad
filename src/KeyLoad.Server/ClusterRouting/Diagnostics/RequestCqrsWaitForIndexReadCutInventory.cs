using KeyLoad.Clustering;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class RequestCqrsWaitForIndexReadCutInventory
{
    internal const string Kind = "request-wait-for-index-read-cut";
    private const string Prefix = "wait-cut-";
    private const string Suffix = ".json";
    private const int IdentifierHexCharacters = 32;
    private const int IdentifierSeparatorCharacters = 1;
    private const int IdentifierPairCharacters = IdentifierHexCharacters + IdentifierSeparatorCharacters + IdentifierHexCharacters;
    private const int RequestIdentifierOffset = IdentifierHexCharacters + IdentifierSeparatorCharacters;
    private const int Sha256HexCharacters = 64;
    private const long MinimumReadPosition = 0;
    private const long MinimumPolicyEpoch = 1;
    private const long MinimumSchemaVersion = 1;
    private const int ExactWitnessCount = 1;
    private const char IdentifierSeparator = '-';
    private const string IdentifierFormat = "N";
    private static readonly string[] FieldNames =
    [
        nameof(RequestCqrsWaitForIndexReadCutWitness.Version),
        nameof(RequestCqrsWaitForIndexReadCutWitness.Kind),
        nameof(RequestCqrsWaitForIndexReadCutWitness.SessionId),
        nameof(RequestCqrsWaitForIndexReadCutWitness.ArmId),
        nameof(RequestCqrsWaitForIndexReadCutWitness.RequestId),
        nameof(RequestCqrsWaitForIndexReadCutWitness.CommandId),
        nameof(RequestCqrsWaitForIndexReadCutWitness.ConnectionId),
        nameof(RequestCqrsWaitForIndexReadCutWitness.PrincipalId),
        nameof(RequestCqrsWaitForIndexReadCutWitness.Voter),
        nameof(RequestCqrsWaitForIndexReadCutWitness.SiloAddress),
        nameof(RequestCqrsWaitForIndexReadCutWitness.GrainDigest),
        nameof(RequestCqrsWaitForIndexReadCutWitness.ActivationId),
        nameof(RequestCqrsWaitForIndexReadCutWitness.NodeId),
        nameof(RequestCqrsWaitForIndexReadCutWitness.Incarnation),
        nameof(RequestCqrsWaitForIndexReadCutWitness.ReadGeneration),
        nameof(RequestCqrsWaitForIndexReadCutWitness.AppliedIncarnation),
        nameof(RequestCqrsWaitForIndexReadCutWitness.AppliedAtomicPartitionId),
        nameof(RequestCqrsWaitForIndexReadCutWitness.AppliedPosition),
        nameof(RequestCqrsWaitForIndexReadCutWitness.AppliedOwnershipEpoch),
        nameof(RequestCqrsWaitForIndexReadCutWitness.StorePosition),
        nameof(RequestCqrsWaitForIndexReadCutWitness.PolicyEpoch),
        nameof(RequestCqrsWaitForIndexReadCutWitness.SchemaVersion),
        nameof(RequestCqrsWaitForIndexReadCutWitness.NativeRequestDigest),
        nameof(RequestCqrsWaitForIndexReadCutWitness.Partition),
    ];
    internal static ReadOnlySpan<string> Fields => FieldNames;
    internal static string Name(Guid arm, Guid request)
        => string.Concat(Prefix, arm.ToString(IdentifierFormat), IdentifierSeparator.ToString(), request.ToString(IdentifierFormat), Suffix);
    internal static bool IsName(string name)
    {
        if (name.Length != Prefix.Length + IdentifierPairCharacters + Suffix.Length
            || !name.StartsWith(Prefix, StringComparison.Ordinal) || !name.EndsWith(Suffix, StringComparison.Ordinal))
        {
            return false;
        }
        var ids = name.AsSpan(Prefix.Length, IdentifierPairCharacters);
        return ids[IdentifierHexCharacters] == IdentifierSeparator && Guid.TryParseExact(ids[..IdentifierHexCharacters], IdentifierFormat, out var arm)
            && Guid.TryParseExact(ids[RequestIdentifierOffset..], IdentifierFormat, out var request) && arm != Guid.Empty && request != Guid.Empty
            && Name(arm, request) == name;
    }
    internal static void Require(RequestCqrsWaitForIndexReadCutWitness value)
    {
        if (value.Version != RequestCqrsProbeProtocol.Version || value.Kind != Kind
            || !RequestCqrsProbeOptionsReader.IsSessionId(value.SessionId) || value.ArmId == Guid.Empty
            || value.RequestId == Guid.Empty || value.CommandId != Guid.Empty || value.ConnectionId == Guid.Empty
            || value.NodeId == Guid.Empty || value.Incarnation == Guid.Empty || value.ReadGeneration < MinimumReadPosition
            || value.StorePosition < MinimumReadPosition || value.AppliedIncarnation != value.Incarnation
            || value.AppliedAtomicPartitionId != value.Partition.ToPartition().AtomicPartitionId
            || value.AppliedPosition < MinimumReadPosition || value.AppliedOwnershipEpoch < MinimumReadPosition
            || value.PolicyEpoch < MinimumPolicyEpoch || value.SchemaVersion < MinimumSchemaVersion
            || string.IsNullOrWhiteSpace(value.PrincipalId) || string.IsNullOrWhiteSpace(value.Voter)
            || string.IsNullOrWhiteSpace(value.SiloAddress) || string.IsNullOrWhiteSpace(value.ActivationId)
            || !Digest(value.GrainDigest) || !Digest(value.NativeRequestDigest))
        {
            throw Invalid();
        }
        RequestCqrsProbeActivationValidation.RequireActivationId(value.ActivationId);
    }
    internal static void RequireInventory(IEnumerable<RequestCqrsWaitForIndexReadCutWitness> values,
        IReadOnlyList<RequestCqrsProbeMarkerRecord> markers)
    {
        var witnesses = values.ToArray();
        foreach (var value in witnesses)
        {
            Require(value);
            RequireSource(value, markers);
            if (markers.Count(marker => marker.SessionId == value.SessionId
                && marker.ArmId == value.ArmId && marker.RequestId == value.RequestId
                && marker.CommandId == value.CommandId && marker.Voter == value.Voter
                && marker.SiloAddress == value.SiloAddress && marker.Phase == RequestCqrsProbePhase.WaitForIndexReadCutCaptured
                && marker.Outcome == RequestCqrsProbeOutcome.Observed) != ExactWitnessCount)
            {
                throw Invalid();
            }
        }
        if (markers.Count(marker => marker.Phase == RequestCqrsProbePhase.WaitForIndexReadCutCaptured) != witnesses.Length)
        {
            throw Invalid();
        }
    }
    internal static void RequireSource(RequestCqrsWaitForIndexReadCutWitness value,
        IReadOnlyList<RequestCqrsProbeMarkerRecord> markers)
    {
        foreach (var outcome in new[] { RequestCqrsProbeOutcome.Observed, RequestCqrsProbeOutcome.Released })
        {
            if (markers.Count(marker => marker.SessionId == value.SessionId && marker.ArmId == value.ArmId
                && marker.RequestId == value.RequestId && marker.CommandId == value.CommandId
                && marker.Voter == value.Voter && marker.SiloAddress == value.SiloAddress
                && marker.Phase == RequestCqrsProbePhase.RequestStarted && marker.Outcome == outcome) != ExactWitnessCount)
            {
                throw Invalid();
            }
        }
    }
    internal static void RequireArm(RequestCqrsWaitForIndexReadCutWitness value, RequestCqrsProbeArmRecord arm)
    {
        if (arm.SessionId != value.SessionId || arm.ArmId != value.ArmId || arm.CommandId != value.CommandId
            || arm.PrincipalId != value.PrincipalId || arm.ReadKind != GrainReadKind.WaitForIndex
            || arm.Phase != RequestCqrsProbePhase.RequestStarted || arm.Action != RequestCqrsProbeAction.Hold
            || arm.TargetVoter is { } target && target != value.Voter)
        {
            throw Invalid();
        }
    }
    private static bool Digest(string value) => value is { Length: Sha256HexCharacters } && value.All(Uri.IsHexDigit);
    private static InvalidOperationException Invalid() => new(RequestCqrsProbeProtocol.InvalidRecord);
}
