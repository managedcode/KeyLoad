namespace KeyLoad.Server.Features.ClusterRouting;

internal static class RequestCqrsCanonicalSourceArmValidation
{
    internal static void Require(RequestCqrsProbeSnapshot snapshot, RequestCqrsProbeArmRecord arm)
    {
        if (!snapshot.Arms.Any(source => source.Record.ArmId == arm.SourceArmId
            && source.Record.SessionId == arm.SessionId && source.Record.PrincipalId == arm.PrincipalId
            && source.Record.CommandId == arm.CommandId && source.Record.ReadKind is null
            && source.Record.Phase == RequestCqrsProbePhase.BeforeSubmit && source.Record.Action == RequestCqrsProbeAction.Hold))
        { throw new InvalidOperationException(RequestCqrsProbeProtocol.InvalidFiles); }
    }
}
