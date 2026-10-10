using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class RequestCqrsSampleChunkProbePair
{
    private const int PairCount = 2;
    private const int OneAdjunct = 1;

    internal static bool ValidAdjunct(RequestCqrsProbeArmRecord arm)
        => arm.Phase == RequestCqrsProbePhase.AuthorizationReload && arm.Action == RequestCqrsProbeAction.Hold
            && arm.ReadKind is null && arm.CommandId != Guid.Empty && arm.SourceRequestId is null
            && arm.SourceArmId is { } source && source != Guid.Empty && source != arm.ArmId
            && arm.Partition is null && arm.TargetVoter is null;

    internal static void RequireDeclaredPair(RequestCqrsProbeArmRecord adjunct,
        IReadOnlyList<RequestCqrsProbeLoadedArm> all)
    {
        if (adjunct.Phase != RequestCqrsProbePhase.AuthorizationReload || adjunct.SourceArmId is null)
        { return; }
        var source = all.FirstOrDefault(arm => arm.Record.ArmId == adjunct.SourceArmId)?.Record;
        if (!ValidAdjunct(adjunct) || all.Count(arm => arm.Record.ArmId == adjunct.SourceArmId) != OneAdjunct
            || source is not { } original || !SameOriginal(original, adjunct)
            || all.Count(arm => arm.Record.SourceArmId == original.ArmId
                && arm.Record.Phase == RequestCqrsProbePhase.AuthorizationReload) != OneAdjunct)
        { throw Invalid(); }
    }

    internal static RequestCqrsProbeLoadedArm Select(RequestCqrsProbeLoadedArm[] matches, GrainRequestPhase phase)
    {
        if (matches.Length != PairCount)
        { throw Invalid(); }
        var primary = matches.FirstOrDefault(arm => arm.Record.Phase == RequestCqrsProbePhase.SampleChunkNativeJobReturned);
        var adjunct = matches.FirstOrDefault(arm => arm.Record.Phase == RequestCqrsProbePhase.AuthorizationReload);
        if (matches.Count(arm => arm.Record.Phase == RequestCqrsProbePhase.SampleChunkNativeJobReturned) != OneAdjunct
            || matches.Count(arm => arm.Record.Phase == RequestCqrsProbePhase.AuthorizationReload) != OneAdjunct
            || primary is null || adjunct is null || !ValidAdjunct(adjunct.Record)
            || !SameOriginal(primary.Record, adjunct.Record))
        { throw Invalid(); }
        return phase switch
        {
            GrainRequestPhase.SampleChunkAdmissionPersisted or GrainRequestPhase.SampleChunkNativeJobReturned
                or GrainRequestPhase.SampleChunkAdmissionRefused => primary,
            GrainRequestPhase.RequestStarted or GrainRequestPhase.AuthorizationReload
                or GrainRequestPhase.BeforeSubmit or GrainRequestPhase.SubmitReturned => adjunct,
            _ => throw Invalid()
        };
    }

    private static bool SameOriginal(RequestCqrsProbeArmRecord primary, RequestCqrsProbeArmRecord adjunct)
        => primary.Phase == RequestCqrsProbePhase.SampleChunkNativeJobReturned && primary.Action == RequestCqrsProbeAction.Hold
            && primary.ReadKind is null && primary.SourceArmId is null && primary.SourceRequestId is null
            && primary.Partition is null && primary.TargetVoter is null && adjunct.SourceArmId == primary.ArmId
            && primary.PrincipalId == adjunct.PrincipalId && primary.CommandId == adjunct.CommandId
            && primary.SessionId == adjunct.SessionId;

    private static InvalidOperationException Invalid() => new(RequestCqrsProbeProtocol.InvalidFiles);
}
