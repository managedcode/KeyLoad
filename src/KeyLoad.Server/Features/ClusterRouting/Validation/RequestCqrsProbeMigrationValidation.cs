namespace KeyLoad.Server.Features.ClusterRouting;

internal static class RequestCqrsProbeMigrationValidation
{
    internal static RequestCqrsProbeActivationRecord Witness(RequestCqrsProbeMigrationRecord value)
        => new(value.Version, RequestCqrsProbeActivationProtocol.Kind, value.SessionId, value.ArmId,
            value.RequestId, value.CommandId, value.Voter, value.SiloAddress, value.GrainDigest, value.ActivationId);

    internal static void Require(RequestCqrsProbeMigrationRecord value)
    {
        RequestCqrsProbeActivationValidation.Require(Witness(value));
        if (value.Kind is not (RequestCqrsProbeMigrationProtocol.RequestKind or RequestCqrsProbeMigrationProtocol.RequestedKind)
            || string.IsNullOrWhiteSpace(value.TargetVoter) || string.IsNullOrWhiteSpace(value.TargetSiloAddress))
        { throw Invalid(); }
        try
        {
            var address = SiloAddress.FromParsableString(value.TargetSiloAddress);
            if (address.ToParsableString() != value.TargetSiloAddress)
            { throw Invalid(); }
        }
        catch (Exception error) when (error is ArgumentException or FormatException)
        { throw Invalid(); }
    }

    internal static string Name(RequestCqrsProbeMigrationRecord value)
        => string.Concat(value.Kind == RequestCqrsProbeMigrationProtocol.RequestKind
            ? RequestCqrsProbeMigrationProtocol.RequestPrefix : RequestCqrsProbeMigrationProtocol.RequestedPrefix,
            value.ArmId.ToString(RequestCqrsProbeProtocol.SessionIdFormat), RequestCqrsProbeActivationProtocol.Separator,
            value.RequestId.ToString(RequestCqrsProbeProtocol.SessionIdFormat), RequestCqrsProbeProtocol.JsonFileSuffix);

    internal static bool IsName(string name)
    {
        var prefix = name.StartsWith(RequestCqrsProbeMigrationProtocol.RequestedPrefix, StringComparison.Ordinal)
            ? RequestCqrsProbeMigrationProtocol.RequestedPrefix : RequestCqrsProbeMigrationProtocol.RequestPrefix;
        if (!name.StartsWith(prefix, StringComparison.Ordinal) || !name.EndsWith(RequestCqrsProbeProtocol.JsonFileSuffix, StringComparison.Ordinal))
        { return false; }
        var parts = name[prefix.Length..^RequestCqrsProbeProtocol.JsonFileSuffix.Length].Split(RequestCqrsProbeActivationProtocol.Separator);
        return parts.Length == RequestCqrsProbeActivationProtocol.IdentityParts
            && CanonicalGuid(parts[RequestCqrsProbeActivationProtocol.FirstPart])
            && CanonicalGuid(parts[RequestCqrsProbeActivationProtocol.SecondPart]);
    }

    private static bool CanonicalGuid(string value)
        => Guid.TryParseExact(value, RequestCqrsProbeProtocol.SessionIdFormat, out var identity)
            && identity != Guid.Empty && identity.ToString(RequestCqrsProbeProtocol.SessionIdFormat) == value;
    private static InvalidOperationException Invalid() => new(RequestCqrsProbeProtocol.InvalidRecord);
}
