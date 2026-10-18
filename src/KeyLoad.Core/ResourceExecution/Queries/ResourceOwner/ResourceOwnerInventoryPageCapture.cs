using System.Collections.Immutable;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private ResourceOwnerInventoryPageV1 CaptureInventoryPage(IKeyValueView view, ResourceOwnerInventoryControlV1 control,
        ResourceOwnerInventoryOwnerCutV1 cut, PrincipalRecord principal, ResourceOwnerInventoryCursorV1? cursor,
        ReadExecutionBudget work)
    {
        var entries = ImmutableArray.CreateBuilder<ResourceOwnerInventoryEntryV1>();
        byte[]? last = null;
        var scan = view.VisitRange(ResourceOwnerInventoryCatalog.Prefix(control.Lifetime), control.Request.PageSize,
            (key, value) =>
            {
                var enrolled = ResourceOwnerInventoryCatalog.ReadEntry(key, value, control.Lifetime, Limits);
                if (!ResourceOwnerInventoryReadValidation.Same(enrolled.Lifetime, control.Lifetime))
                {
                    throw Errors.Fail(ErrorCode.Corruption, ResourceOwnerInventoryReadValidation.Malformed);
                }
                entries.Add(ResolveInventoryEntry(control, cut, enrolled.Partition));
                last = key.ToArray();
                return true;
            }, afterKey: cursor?.AfterKey.ToArray(), cancellationToken: work.Cancellation);
        if (scan.StoppedByVisitor || scan.HasMore && last is null)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceOwnerInventoryReadValidation.Malformed);
        }
        ResourceOwnerInventoryCursorV1? next = scan.HasMore
            ? new(control.Request.InventoryId, control.Lifetime.Incarnation, cut, last!, principal.Id,
                principal.PolicyEpoch, control.Request.ExpiresAt) : null;
        return new(cut, entries.ToImmutable(), next);
    }
}
