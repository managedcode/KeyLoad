using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal ResourceOwnerInventoryControlV1 CaptureRetiringResourceOwnerInventoryControl(string principalId,
        ResourceRetirementControlV1 control, ResourceRetirementAttemptV1 attempt, Guid inventoryId, int pageSize,
        DateTimeOffset expiresAt, ReadExecutionBudget work)
    {
        ResourceOwnerInventoryReadValidation.Expiry(expiresAt, EvaluationClock.GetUtcNow(), work);
        var result = Store.Read(raw =>
        {
            var view = work.CreateView(raw);
            var principal = RequireInventoryAdministrator(view, principalId);
            var state = RequireRetirementSource(view, control);
            ResourceRetirementStateValidation.AllOwners(state);
            var stored = ResourceRetirementAttemptStorage.RequirePrepared(view, control, attempt, Limits);
            if (inventoryId != stored.OriginalCommandId || expiresAt > stored.ExpiresAt
                || stored.ExpiresAt <= EvaluationClock.GetUtcNow()
                || stored.PrincipalId != principal.Id || stored.PolicyEpoch != principal.PolicyEpoch)
            {
                throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceRetirementProtocol.Changed);
            }
            var request = new ResourceOwnerInventoryRequestV1(ResourceOwnerInventoryReadValidation.Version,
                inventoryId, control.Request.TenantId, control.Request.DatabaseId, control.Request.ResourceName,
                control.Lifetime.Incarnation, pageSize, expiresAt);
            ResourceOwnerInventoryReadValidation.Request(request, Limits);
            var captured = CaptureInventoryControl(view, principal, request, initial: false,
                retiring: true, retirementAttempt: stored);
            ResourceRetirementValidation.Bound(captured, Limits);
            return captured;
        });
        work.CheckResult(result);
        work.Check();
        return result;
    }
}
