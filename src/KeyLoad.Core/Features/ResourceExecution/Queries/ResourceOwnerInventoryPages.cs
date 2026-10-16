using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal ResourceOwnerInventoryPageV1 ReadResourceOwnerInventoryPage(string principalId,
        ResourceOwnerInventoryControlV1 control, RegisteredPhysicalOwnerV1 observedOwner,
        ResourceOwnerInventoryCursorV1? continuation, ReadExecutionBudget work)
    {
        work.Check();
        var page = Store.Read(raw =>
        {
            var view = work.CreateView(raw);
            var principal = RequireInventoryAdministrator(view, principalId);
            ResourceOwnerInventoryReadValidation.Control(control, Limits);
            ResourceOwnerInventoryReadValidation.Expiry(control.Request.ExpiresAt, EvaluationClock.GetUtcNow(), work);
            if (continuation is not null)
            {
                ResourceOwnerInventoryCursorValidation.Require(continuation, control, principal, Limits);
            }
            var cut = CaptureInventoryOwnerCut(view, control, observedOwner);
            if (continuation is not null)
            {
                ResourceOwnerInventoryReadValidation.Cut(continuation.Cut, cut);
                RequireInventoryAfterKey(view, control, continuation);
            }
            return CaptureInventoryPage(view, control, cut, principal, continuation, work);
        });
        ResourceOwnerInventoryReadValidation.Bound(page, Limits);
        work.CheckResult(page);
        work.Check();
        return page;
    }

    internal void ValidateResourceOwnerInventoryOwner(string principalId, ResourceOwnerInventoryControlV1 control,
        ResourceOwnerInventoryOwnerCutV1 cut, ReadExecutionBudget work)
    {
        work.Check();
        Store.Read(raw =>
        {
            var view = work.CreateView(raw);
            _ = RequireInventoryAdministrator(view, principalId);
            ResourceOwnerInventoryReadValidation.Control(control, Limits);
            ResourceOwnerInventoryReadValidation.Expiry(control.Request.ExpiresAt, EvaluationClock.GetUtcNow(), work);
            ResourceOwnerInventoryReadValidation.OwnerCut(cut, Limits);
            var observed = control.Directory.Owners.SingleOrDefault(entry =>
                PhysicalOwnerEntryValidation.SameOwner(entry.Owner, cut.ObservedOwner))
                ?? throw Errors.Fail(ErrorCode.OwnershipLost, ResourceOwnerInventoryReadValidation.Changed);
            ResourceOwnerInventoryReadValidation.Cut(cut, CaptureInventoryOwnerCut(view, control, observed));
            return true;
        });
        work.Check();
    }

    private void RequireInventoryAfterKey(IKeyValueView view, ResourceOwnerInventoryControlV1 control,
        ResourceOwnerInventoryCursorV1 cursor)
    {
        var found = view.ReadValue(cursor.AfterKey.ToArray(), value =>
        {
            _ = ResourceOwnerInventoryCatalog.ReadEntry(cursor.AfterKey.Span, value, control.Lifetime, Limits);
        });
        if (!found)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceOwnerInventoryReadValidation.Changed);
        }
    }
}
