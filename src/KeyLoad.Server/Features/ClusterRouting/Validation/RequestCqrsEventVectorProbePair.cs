using KeyLoad.Clustering;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Correlates only the two original prepared first-Admit observation arms.</summary>
internal static class RequestCqrsEventVectorProbePair
{
    private const int ExactlyOne = 1;

    internal static bool ValidArm(RequestCqrsProbeArmRecord arm)
        => arm.Action == RequestCqrsProbeAction.Hold && arm.ReadKind is null
            && arm.CommandId != Guid.Empty && arm.Partition is null && arm.TargetVoter is null
            && (arm.Phase == RequestCqrsProbePhase.EventVectorFirstCapturesReady
                ? arm.SourceArmId is null && arm.SourceRequestId is null
                : arm.Phase == RequestCqrsProbePhase.EventVectorFirstAdmitPrepared
                    && arm.SourceArmId is { } source && source != Guid.Empty && source != arm.ArmId
                    && arm.SourceRequestId is { } request && request != Guid.Empty);

    internal static bool Eligible(RequestCqrsProbeArmRecord arm, GrainRequestProbeIdentity identity,
        IReadOnlyList<RequestCqrsProbeMarkerRecord> markers)
    {
        if (arm.Phase is not (RequestCqrsProbePhase.EventVectorFirstCapturesReady
            or RequestCqrsProbePhase.EventVectorFirstAdmitPrepared))
        {
            return true;
        }
        if (identity.CommandKind != OperationKind.EventFeedControl
            || arm.Phase == RequestCqrsProbePhase.EventVectorFirstAdmitPrepared
                && arm.SourceRequestId == identity.RequestId)
        {
            return false;
        }
        if (arm.Phase == RequestCqrsProbePhase.EventVectorFirstAdmitPrepared)
        {
            RequireObserved(arm, markers);
        }
        return true;
    }

    internal static void RequireDeclaredPair(RequestCqrsProbeArmRecord adjunct, IReadOnlyList<RequestCqrsProbeLoadedArm> arms)
    {
        if (adjunct.Phase != RequestCqrsProbePhase.EventVectorFirstAdmitPrepared)
        {
            return;
        }
        var sources = arms.Where(arm => arm.Record.ArmId == adjunct.SourceArmId).ToArray();
        if (!ValidArm(adjunct) || sources.Length != ExactlyOne
            || !SameOriginal(sources.Single().Record, adjunct)
            || arms.Count(arm => arm.Record.Phase == adjunct.Phase
                && arm.Record.SourceArmId == adjunct.SourceArmId) != ExactlyOne)
        {
            throw Invalid();
        }
    }

    private static void RequireObserved(RequestCqrsProbeArmRecord adjunct,
        IReadOnlyList<RequestCqrsProbeMarkerRecord> markers)
    {
        var observed = markers.Where(marker => marker.ArmId == adjunct.SourceArmId
            && marker.Phase == RequestCqrsProbePhase.EventVectorFirstCapturesReady
            && marker.Outcome == RequestCqrsProbeOutcome.Observed).ToArray();
        if (observed.Length != ExactlyOne || observed.Single().RequestId != adjunct.SourceRequestId
            || observed.Single().CommandId != adjunct.CommandId)
        {
            throw Invalid();
        }
    }

    private static bool SameOriginal(RequestCqrsProbeArmRecord primary, RequestCqrsProbeArmRecord adjunct)
        => primary.Phase == RequestCqrsProbePhase.EventVectorFirstCapturesReady
            && ValidArm(primary) && primary.SessionId == adjunct.SessionId
            && primary.PrincipalId == adjunct.PrincipalId && primary.CommandId == adjunct.CommandId;

    private static InvalidOperationException Invalid() => new(RequestCqrsProbeProtocol.InvalidFiles);
}
