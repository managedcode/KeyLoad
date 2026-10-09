namespace KeyLoad.Server.Features.ClusterRouting;

internal static class RequestCqrsProbeActivationValidation
{
    internal static void Require(RequestCqrsProbeActivationRecord value)
    {
        if (value.Version != RequestCqrsProbeProtocol.Version || value.Kind != RequestCqrsProbeActivationProtocol.Kind
            || !RequestCqrsProbeOptionsReader.IsSessionId(value.SessionId) || value.ArmId == Guid.Empty
            || value.RequestId == Guid.Empty || value.CommandId == Guid.Empty
            || string.IsNullOrWhiteSpace(value.Voter) || string.IsNullOrWhiteSpace(value.SiloAddress)
            || value.GrainDigest.Length != RequestCqrsProbeActivationProtocol.DigestCharacters
            || value.GrainDigest.Any(character => !char.IsAsciiHexDigitLower(character)))
        { throw Invalid(); }
        try
        {
            var identity = ActivationId.FromParsableString(value.ActivationId);
            if (identity.IsDefault || identity.ToParsableString() != value.ActivationId)
            { throw Invalid(); }
        }
        catch (Exception error) when (error is FormatException or ArgumentException)
        { throw Invalid(); }
    }

    internal static string Name(RequestCqrsProbeActivationRecord value) => Name(value.ArmId, value.RequestId);

    internal static string Name(Guid armId, Guid requestId)
        => RequestCqrsProbeActivationProtocol.Prefix + armId.ToString(RequestCqrsProbeProtocol.SessionIdFormat)
            + RequestCqrsProbeActivationProtocol.Separator + requestId.ToString(RequestCqrsProbeProtocol.SessionIdFormat)
            + RequestCqrsProbeProtocol.JsonFileSuffix;

    internal static bool IsName(string name)
    {
        if (!name.StartsWith(RequestCqrsProbeActivationProtocol.Prefix, StringComparison.Ordinal)
            || !name.EndsWith(RequestCqrsProbeProtocol.JsonFileSuffix, StringComparison.Ordinal))
        { return false; }
        var parts = name[RequestCqrsProbeActivationProtocol.Prefix.Length..^RequestCqrsProbeProtocol.JsonFileSuffix.Length]
            .Split(RequestCqrsProbeActivationProtocol.Separator, StringSplitOptions.None);
        return parts.Length == RequestCqrsProbeActivationProtocol.IdentityParts
            && CanonicalGuid(parts[RequestCqrsProbeActivationProtocol.FirstPart])
            && CanonicalGuid(parts[RequestCqrsProbeActivationProtocol.SecondPart]);
    }

    internal static void RequireMarker(RequestCqrsProbeActivationRecord value, IReadOnlyList<RequestCqrsProbeMarkerRecord> markers)
    {
        Require(value);
        var matches = markers.Where(marker => marker.ArmId == value.ArmId && marker.RequestId == value.RequestId
            && marker.Phase == RequestCqrsProbePhase.BeforeSubmit && marker.Outcome == RequestCqrsProbeOutcome.Observed).ToArray();
        if (matches.Length != RequestCqrsProbeActivationProtocol.SingleMarker
            || matches[RequestCqrsProbeActivationProtocol.FirstPart].SessionId != value.SessionId
            || matches[RequestCqrsProbeActivationProtocol.FirstPart].CommandId != value.CommandId
            || matches[RequestCqrsProbeActivationProtocol.FirstPart].Voter != value.Voter
            || matches[RequestCqrsProbeActivationProtocol.FirstPart].SiloAddress != value.SiloAddress)
        { throw Invalid(); }
    }

    private static bool CanonicalGuid(string text)
        => Guid.TryParseExact(text, RequestCqrsProbeProtocol.SessionIdFormat, out var id)
            && id != Guid.Empty && id.ToString(RequestCqrsProbeProtocol.SessionIdFormat) == text;

    private static InvalidOperationException Invalid() => new(RequestCqrsProbeProtocol.InvalidRecord);
}
