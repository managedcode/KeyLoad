using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Admits only the exact durable retiring inventory context; ordinary active inventory stays fenced.</summary>
internal static class ResourceRetirementInventoryAdmission
{
    internal static bool IsSourceRetiringControl(IKeyValueView view, ResourceOwnerInventoryControlV1 control,
        DatabaseLimits limits)
    {
        if (ResourceRetirementStorage.ReadControl(view, control.Lifetime, limits) is null)
        {
            return false;
        }
        RequireSourceContext(view, control, limits);
        return true;
    }

    internal static void RequireSourceContext(IKeyValueView view, ResourceOwnerInventoryControlV1 control,
        DatabaseLimits limits)
    {
        var state = ResourceRetirementStorage.ReadControl(view, control.Lifetime, limits)
            ?? throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceRetirementProtocol.Changed);
        ResourceRetirementStateValidation.AllOwners(state);
        var attempt = control.RetirementAttempt
            ?? throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceRetirementProtocol.Changed);
        ResourceRetirementAttemptStorage.RequirePrepared(view, state.Control, attempt, limits);
        ResourceRetirementInventoryValidation.SameSource(control, state.Control);
        var fence = ResourceRetirementStorage.ReadFence(view, control.Lifetime, limits)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementProtocol.Incomplete);
        SameControl(state.Control, fence.Control);
    }

    internal static void RequireOwnerContext(IKeyValueView view, ResourceOwnerInventoryControlV1 control,
        DatabaseLimits limits)
    {
        var state = ResourceRetirementStorage.ReadControl(view, control.Lifetime, limits);
        var fence = ResourceRetirementStorage.ReadFence(view, control.Lifetime, limits);
        if (state is null && fence is null)
        {
            return;
        }
        if (fence is null)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementProtocol.Incomplete);
        }
        ResourceRetirementAttemptValidation.Witness(control.RetirementAttempt
            ?? throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceRetirementProtocol.Changed), fence.Control, limits);
        ResourceRetirementInventoryValidation.SameSource(control, fence.Control);
        if (state is not null)
        {
            ResourceRetirementStateValidation.AllOwners(state);
            SameControl(state.Control, fence.Control);
        }
    }

    private static void SameControl(ResourceRetirementControlV1 left, ResourceRetirementControlV1 right)
    {
        if (!ResourceRetirementValidation.Same(left, right))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceRetirementProtocol.Changed);
        }
    }
}
