using System.Globalization;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class RequestCqrsProbeLiveValidation
{
    private const int EmptyDigestLength = 0;
    private const char FirstDigit = '0';
    private const char LastDigit = '9';
    private const char FirstHex = 'a';
    private const char LastHex = 'f';
    internal static RequestCqrsProbeActivationRecord Witness(RequestCqrsProbeLiveRecord value)
        => new(value.Version, RequestCqrsProbeActivationProtocol.Kind, value.SessionId, value.ArmId,
            value.RequestId, value.CommandId, value.Voter, value.SiloAddress, value.GrainDigest, value.ActivationId);

    internal static void Require(RequestCqrsProbeLiveRecord value)
    {
        RequestCqrsProbeActivationValidation.Require(Witness(value));
        if (value.Nonce == Guid.Empty || value.Step is not (RequestCqrsProbeLiveProtocol.BeforeReplacement
            or RequestCqrsProbeLiveProtocol.AfterReplacement)
            || value.Kind is not (RequestCqrsProbeLiveProtocol.ChallengeKind or RequestCqrsProbeLiveProtocol.AckKind)
            || value.PredecessorSha256 is not { Length: EmptyDigestLength } && !Digest(value.PredecessorSha256)
            || value.Kind == RequestCqrsProbeLiveProtocol.ChallengeKind
                && value.ChallengeSha256 is not { Length: EmptyDigestLength }
            || value.Kind == RequestCqrsProbeLiveProtocol.AckKind && !Digest(value.ChallengeSha256)
            || value.Step == RequestCqrsProbeLiveProtocol.AfterReplacement && !Digest(value.PredecessorSha256))
        { throw new InvalidOperationException(RequestCqrsProbeProtocol.InvalidRecord); }
    }

    internal static string Name(RequestCqrsProbeLiveRecord value)
        => string.Concat(value.Kind == RequestCqrsProbeLiveProtocol.ChallengeKind
            ? RequestCqrsProbeLiveProtocol.ChallengePrefix : RequestCqrsProbeLiveProtocol.AckPrefix,
            value.ArmId.ToString(RequestCqrsProbeProtocol.SessionIdFormat), RequestCqrsProbeActivationProtocol.Separator,
            value.RequestId.ToString(RequestCqrsProbeProtocol.SessionIdFormat), RequestCqrsProbeActivationProtocol.Separator,
            value.Step.ToString(CultureInfo.InvariantCulture), RequestCqrsProbeProtocol.JsonFileSuffix);

    internal static bool IsName(string name)
    {
        var prefix = name.StartsWith(RequestCqrsProbeLiveProtocol.ChallengePrefix, StringComparison.Ordinal)
            ? RequestCqrsProbeLiveProtocol.ChallengePrefix : RequestCqrsProbeLiveProtocol.AckPrefix;
        if (!name.StartsWith(prefix, StringComparison.Ordinal)
            || !name.EndsWith(RequestCqrsProbeProtocol.JsonFileSuffix, StringComparison.Ordinal))
        { return false; }
        var parts = name[prefix.Length..^RequestCqrsProbeProtocol.JsonFileSuffix.Length].Split(RequestCqrsProbeActivationProtocol.Separator);
        return parts.Length == RequestCqrsProbeLiveProtocol.NameParts
            && Guid.TryParseExact(parts[RequestCqrsProbeActivationProtocol.FirstPart], RequestCqrsProbeProtocol.SessionIdFormat, out var arm)
            && arm != Guid.Empty && arm.ToString(RequestCqrsProbeProtocol.SessionIdFormat) == parts[RequestCqrsProbeActivationProtocol.FirstPart]
            && Guid.TryParseExact(parts[RequestCqrsProbeActivationProtocol.SecondPart], RequestCqrsProbeProtocol.SessionIdFormat, out var request)
            && request != Guid.Empty && request.ToString(RequestCqrsProbeProtocol.SessionIdFormat) == parts[RequestCqrsProbeActivationProtocol.SecondPart]
            && parts[RequestCqrsProbeLiveProtocol.StepPart] is RequestCqrsProbeLiveProtocol.BeforeSuffix or RequestCqrsProbeLiveProtocol.AfterSuffix;
    }

    private static bool Digest(string value) => value.Length == RequestCqrsProbeActivationProtocol.DigestCharacters
        && value.All(character => character is >= FirstDigit and <= LastDigit or >= FirstHex and <= LastHex);
}
