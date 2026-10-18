using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal ResourceRetirementPreparationStateV1 ReadResourceRetirementPreparation(string principalId,
        ResourceRetirementRequestV1 request, ReadExecutionBudget work)
    {
        work.Check();
        var result = Store.Read(raw =>
        {
            var view = work.CreateView(raw);
            RequireInventoryAdministrator(view, principalId);
            ResourceRetirementValidation.Request(request, Limits);
            var key = KeyLoad.Storage.KeyCodec.Encode(ResourceRetirementPreparationKeys.StateSpace,
                request.TenantId, request.DatabaseId, request.ResourceName, request.ExpectedIncarnation.Value);
            var state = ResourceRetirementStorage.Read<ResourceRetirementPreparationStateV1>(view, key, Limits)
                ?? throw Errors.Fail(ErrorCode.NotFound, ResourceRetirementProtocol.Missing);
            ResourceRetirementPreparationStateValidation.Require(state, Limits);
            ResourceRetirementAttemptPointers.ValidateStored(view, state, Limits);
            if (!ResourceRetirementValidation.Same(state.Control.Request, request))
            {
                throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceRetirementProtocol.Changed);
            }
            RequirePreparationOutcome(view, state.Control);
            return state;
        });
        work.CheckResult(result);
        work.Check();
        return result;
    }

    internal ResourceRetirementOwnerStatusV1 ReadResourceRetirementOwnerStatus(string principalId,
        ResourceRetirementPreparationControlV1 control, ReadExecutionBudget work)
    {
        work.Check();
        var result = Store.Read(raw =>
        {
            var view = work.CreateView(raw);
            RequireInventoryAdministrator(view, principalId);
            var reservation = ResourceRetirementPreparationStorage.ReadReservation(view, control.Lifetime, Limits)
                ?? throw Errors.Fail(ErrorCode.NotFound, ResourceRetirementProtocol.Missing);
            RequirePreparationReceiver(view, control, reservation.Receiver);
            if (!ResourceRetirementValidation.Same(reservation.Control, control))
            {
                throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceRetirementProtocol.Changed);
            }
            ResourceRetirementPreparationEvidence.Owner(view, reservation, Limits);
            var status = new ResourceRetirementOwnerStatusV1(reservation, CaptureRetirementStatusCut(view), []);
            ResourceRetirementPreparationValidation.Status(status, Limits);
            return status;
        });
        work.CheckResult(result);
        work.Check();
        return result;
    }
}
