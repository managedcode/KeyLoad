using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Server.Features.ResourceExecution;

internal static class ResourceRetirementDurablePhaseValidation
{
    internal static void RequirePrepare(ResourceRetirementNativeRequest outer, ResourceRetirementPhaseCommandV1 phase,
        DatabaseLimits limits)
    {
        var body = NativeSerialization.Deserialize<ResourceRetirementPrepareAttemptBodyV1>(phase.Body.Span);
        if (body?.Context is null || body.AttemptId == Guid.Empty || body.ExpectedOrdinal < 0)
        {
            throw Invalid();
        }
        RequireContext(outer, body.Context, limits);
        if (!PhysicalOwnerEntryValidation.Same(outer.Control, outer.Destination))
        {
            throw Invalid();
        }
    }

    internal static void RequirePrepared(ResourceRetirementNativeRequest outer, ResourceRetirementPhaseCommandV1 phase,
        DatabaseLimits limits)
    {
        var body = NativeSerialization.Deserialize<ResourceRetirementPreparedOperationBodyV1>(phase.Body.Span);
        if (body?.Context is null || body.Attempt is null || !Enum.IsDefined(body.Action))
        {
            throw Invalid();
        }
        RequireContext(outer, body.Context, limits);
        if (!PhysicalOwnerEntryValidation.SameOwner(ResourceRetirementAttemptContextValidation.ExecutionOwner(body.Context), outer.Destination.Owner)
            || (body.Context.Purpose is ResourceRetirementAttemptPurpose.OwnerReservation or ResourceRetirementAttemptPurpose.CleanupPage)
                && !PhysicalOwnerEntryValidation.Same(body.Context.Receiver!, outer.Destination)
            || body.Context.Purpose == ResourceRetirementAttemptPurpose.SealBarrier
                && !PhysicalOwnerEntryValidation.Same(outer.Control, outer.Destination)
            || body.Action == ResourceRetirementPreparedOperationAction.Close && body.Original is not null
            || body.Action == ResourceRetirementPreparedOperationAction.Persist
                && (outer.ExpiresAt > body.Attempt.ExpiresAt
                    || body.Original is null || body.Original.Kind != OperationKind.ResourceRetirementPhase
                    || body.Original.NativePayload.IsEmpty || body.Original.Id != body.Attempt.OriginalCommandId))
        {
            throw Invalid();
        }
    }

    internal static void RequireContext(ResourceRetirementNativeRequest outer, ResourceRetirementAttemptContextV1 context,
        DatabaseLimits limits)
    {
        ResourceRetirementAttemptContextValidation.Require(context, limits);
        var directory = context.Purpose is ResourceRetirementAttemptPurpose.SealBarrier or ResourceRetirementAttemptPurpose.CleanupPage
            ? context.Retirement!.Directory : context.Preparation!.Directory;
        if (!PhysicalOwnerEntryValidation.SameOwner(ResourceRetirementAttemptContextValidation.SourceOwner(context), outer.Control.Owner)
            || !directory.Owners.Any(owner => PhysicalOwnerEntryValidation.Same(owner, outer.Control)))
        {
            throw Invalid();
        }
    }

    private static KeyLoadException Invalid()
        => Errors.Fail(ErrorCode.Unauthenticated, ResourceRetirementNativeProtocol.InvalidProof);
}
