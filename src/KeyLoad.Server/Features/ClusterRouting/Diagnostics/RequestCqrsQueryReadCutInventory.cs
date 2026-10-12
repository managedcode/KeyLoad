using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class RequestCqrsQueryReadCutInventory
{
    internal const string Kind = "request-query-read-cut";
    private const string Prefix = "query-cut-";
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
        nameof(RequestCqrsQueryReadCutWitness.Version),
        nameof(RequestCqrsQueryReadCutWitness.Kind),
        nameof(RequestCqrsQueryReadCutWitness.SessionId),
        nameof(RequestCqrsQueryReadCutWitness.ArmId),
        nameof(RequestCqrsQueryReadCutWitness.RequestId),
        nameof(RequestCqrsQueryReadCutWitness.CommandId),
        nameof(RequestCqrsQueryReadCutWitness.ConnectionId),
        nameof(RequestCqrsQueryReadCutWitness.PrincipalId),
        nameof(RequestCqrsQueryReadCutWitness.Voter),
        nameof(RequestCqrsQueryReadCutWitness.SiloAddress),
        nameof(RequestCqrsQueryReadCutWitness.GrainDigest),
        nameof(RequestCqrsQueryReadCutWitness.ActivationId),
        nameof(RequestCqrsQueryReadCutWitness.NodeId),
        nameof(RequestCqrsQueryReadCutWitness.Incarnation),
        nameof(RequestCqrsQueryReadCutWitness.ReadGeneration),
        nameof(RequestCqrsQueryReadCutWitness.ResultCutPosition),
        nameof(RequestCqrsQueryReadCutWitness.StorePosition),
        nameof(RequestCqrsQueryReadCutWitness.PolicyEpoch),
        nameof(RequestCqrsQueryReadCutWitness.SchemaVersion),
        nameof(RequestCqrsQueryReadCutWitness.QueryHash),
        nameof(RequestCqrsQueryReadCutWitness.Partition),
        nameof(RequestCqrsQueryReadCutWitness.AccessPath),
    ];
    internal static ReadOnlySpan<string> Fields => FieldNames;
    internal static string Name(Guid arm, Guid request)
        => string.Concat(Prefix, arm.ToString(IdentifierFormat), IdentifierSeparator.ToString(), request.ToString(IdentifierFormat), Suffix);
    internal static bool IsName(string name)
    {
        if (name.Length != Prefix.Length + IdentifierPairCharacters + Suffix.Length
            || !name.StartsWith(Prefix, StringComparison.Ordinal) || !name.EndsWith(Suffix, StringComparison.Ordinal))
        { return false; }
        var ids = name.AsSpan(Prefix.Length, IdentifierPairCharacters);
        return ids[IdentifierHexCharacters] == IdentifierSeparator && Guid.TryParseExact(ids[..IdentifierHexCharacters], IdentifierFormat, out var arm)
            && Guid.TryParseExact(ids[RequestIdentifierOffset..], IdentifierFormat, out var request) && arm != Guid.Empty && request != Guid.Empty
            && Name(arm, request) == name;
    }
    internal static void Require(RequestCqrsQueryReadCutWitness value)
    {
        if (value.Version != RequestCqrsProbeProtocol.Version || value.Kind != Kind
            || !RequestCqrsProbeOptionsReader.IsSessionId(value.SessionId) || value.ArmId == Guid.Empty
            || value.RequestId == Guid.Empty || value.CommandId != Guid.Empty || value.ConnectionId == Guid.Empty
            || value.NodeId == Guid.Empty || value.Incarnation == Guid.Empty || value.ReadGeneration < MinimumReadPosition
            || value.ResultCutPosition < MinimumReadPosition || value.ResultCutPosition > value.StorePosition
            || value.PolicyEpoch < MinimumPolicyEpoch || value.SchemaVersion < MinimumSchemaVersion
            || string.IsNullOrWhiteSpace(value.PrincipalId) || string.IsNullOrWhiteSpace(value.Voter)
            || string.IsNullOrWhiteSpace(value.SiloAddress) || string.IsNullOrWhiteSpace(value.ActivationId)
            || !Digest(value.GrainDigest) || !Digest(value.QueryHash) || string.IsNullOrWhiteSpace(value.AccessPath))
        { throw Invalid(); }
        RequestCqrsProbeActivationValidation.RequireActivationId(value.ActivationId);
    }
    internal static void RequireInventory(IEnumerable<RequestCqrsQueryReadCutWitness> values,
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
                && marker.SiloAddress == value.SiloAddress && marker.Phase == RequestCqrsProbePhase.QueryReadCutCaptured
                && marker.Outcome == RequestCqrsProbeOutcome.Observed) != ExactWitnessCount)
            { throw Invalid(); }
        }
        if (markers.Count(marker => marker.Phase == RequestCqrsProbePhase.QueryReadCutCaptured) != witnesses.Length)
        { throw Invalid(); }
    }
    internal static void RequireSource(RequestCqrsQueryReadCutWitness value,
        IReadOnlyList<RequestCqrsProbeMarkerRecord> markers)
    {
        foreach (var outcome in new[] { RequestCqrsProbeOutcome.Observed, RequestCqrsProbeOutcome.Released })
        {
            if (markers.Count(marker => marker.SessionId == value.SessionId && marker.ArmId == value.ArmId
                && marker.RequestId == value.RequestId && marker.CommandId == value.CommandId
                && marker.Voter == value.Voter && marker.SiloAddress == value.SiloAddress
                && marker.Phase == RequestCqrsProbePhase.RequestStarted && marker.Outcome == outcome) != ExactWitnessCount)
            { throw Invalid(); }
        }
    }
    internal static void RequireArm(RequestCqrsQueryReadCutWitness value, RequestCqrsProbeArmRecord arm)
    {
        if (arm.SessionId != value.SessionId || arm.ArmId != value.ArmId || arm.CommandId != value.CommandId
            || arm.PrincipalId != value.PrincipalId || arm.ReadKind != GrainReadKind.AstQuery
            || arm.Phase != RequestCqrsProbePhase.RequestStarted || arm.Action != RequestCqrsProbeAction.Hold
            || arm.TargetVoter is { } target && target != value.Voter)
        { throw Invalid(); }
    }
    private static bool Digest(string value) => value is { Length: Sha256HexCharacters } && value.All(Uri.IsHexDigit);
    private static InvalidOperationException Invalid() => new(RequestCqrsProbeProtocol.InvalidRecord);
}
