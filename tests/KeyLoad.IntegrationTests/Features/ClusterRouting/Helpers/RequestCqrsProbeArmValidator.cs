using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Validates arms before writing their exact bounded source-generated wire shape.</summary>
internal static class RequestCqrsProbeArmValidator
{
    private static readonly System.Text.UTF8Encoding StrictUtf8 = new(false, true);

    internal static void Validate(string principalId, Guid commandId, GrainReadKind? readKind,
        RequestCqrsProbePhase phase, RequestCqrsProbeAction action)
    {
        if (string.IsNullOrWhiteSpace(principalId)
            || !principalId.StartsWith("c1-probe-", StringComparison.Ordinal)
            || StrictUtf8.GetByteCount(principalId) > 256 || !Enum.IsDefined(phase)
            || phase is RequestCqrsProbePhase.ProducerDisposed or RequestCqrsProbePhase.CanonicalOutboundObserved
                or RequestCqrsProbePhase.CanonicalIndependentAppendCompleted or RequestCqrsProbePhase.CanonicalOwnerDisposed || !Enum.IsDefined(action)
            || (readKind is null) == (commandId == Guid.Empty)
            || (readKind is { } read && !Enum.IsDefined(read)))
        { throw new InvalidOperationException(RequestCqrsProbeFixtureProtocol.InvalidArm); }
    }

    internal static void ValidateDecoded(RequestCqrsProbeArmRecord decoded, Guid armId, string principalId,
        Guid commandId, GrainReadKind? readKind, RequestCqrsProbePhase phase, RequestCqrsProbeAction action)
    {
        if (decoded.Version != RequestCqrsProbeFixtureProtocol.Version
            || decoded.Kind != RequestCqrsProbeFixtureProtocol.ArmKind || decoded.ArmId != armId
            || decoded.PrincipalId != principalId || decoded.CommandId != commandId || decoded.ReadKind != readKind
            || decoded.Phase != phase || decoded.Action != action)
        { throw new InvalidOperationException(RequestCqrsProbeFixtureProtocol.InvalidArm); }
    }
}
