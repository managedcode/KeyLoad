using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal ResourceOwnerInventoryControlV1 CaptureResourceOwnerInventoryControl(string principalId,
        ResourceOwnerInventoryRequestV1 request, ReadExecutionBudget work)
    {
        ResourceOwnerInventoryReadValidation.Request(request, Limits);
        ResourceOwnerInventoryReadValidation.Expiry(request.ExpiresAt, EvaluationClock.GetUtcNow(), work);
        var result = Store.Read(raw =>
        {
            var view = work.CreateView(raw);
            var principal = RequireInventoryAdministrator(view, principalId);
            return CaptureInventoryControl(view, principal, request, initial: true);
        });
        work.CheckResult(result);
        work.Check();
        return result;
    }

    internal void ValidateResourceOwnerInventoryControl(string principalId,
        ResourceOwnerInventoryControlV1 control, ReadExecutionBudget work)
    {
        work.Check();
        Store.Read(raw =>
        {
            var view = work.CreateView(raw);
            var principal = RequireInventoryAdministrator(view, principalId);
            ResourceOwnerInventoryReadValidation.Control(control, Limits);
            ResourceOwnerInventoryReadValidation.Expiry(control.Request.ExpiresAt, EvaluationClock.GetUtcNow(), work);
            if (control.PrincipalId != principal.Id || control.PolicyEpoch != principal.PolicyEpoch)
            {
                throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceOwnerInventoryReadValidation.Changed);
            }
            var current = CaptureInventoryControl(view, principal, control.Request, initial: false);
            if (!ResourceOwnerInventoryReadValidation.Same(current.Definition, control.Definition)
                || !ResourceOwnerInventoryReadValidation.Same(current.Lifetime, control.Lifetime))
            {
                throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceOwnerInventoryReadValidation.Changed);
            }
            if (!ResourceOwnerInventoryReadValidation.Same(current.Catalog, control.Catalog)
                || !ResourceOwnerInventoryReadValidation.Same(current.Directory, control.Directory)
                || current.PlacementDirectoryRevision != control.PlacementDirectoryRevision)
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, ResourceOwnerInventoryReadValidation.Changed);
            }
            return true;
        });
        work.Check();
    }

    private PrincipalRecord RequireInventoryAdministrator(IKeyValueView view, string principalId)
    {
        var principal = Principal(view, principalId, EvaluationClock.GetUtcNow());
        if (!principal.ClusterAdministrator)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, ResourceOwnerInventoryReadValidation.Administrator);
        }
        return principal;
    }

    private ResourceOwnerInventoryControlV1 CaptureInventoryControl(IKeyValueView view, PrincipalRecord principal,
        ResourceOwnerInventoryRequestV1 request, bool initial)
    {
        var (definition, lifetime) = ReadInventoryResourcePair(view, request.TenantId,
            request.DatabaseId, request.ResourceName, initial);
        if (lifetime.Incarnation != request.ExpectedIncarnation)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceOwnerInventoryReadValidation.Changed);
        }
        var catalog = ReadInventoryPhysicalCatalog(view);
        var directory = PhysicalOwnerDirectorySerialization.Read(view)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceOwnerInventoryReadValidation.Missing);
        if (!PhysicalOwnerEntryValidation.SameOwner(directory.ControlOwner, catalog.DefaultShard))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceOwnerInventoryReadValidation.Malformed);
        }
        var result = new ResourceOwnerInventoryControlV1(request, definition, lifetime, catalog, directory,
            ReadInventoryPlacementRevision(view), principal.Id, principal.PolicyEpoch);
        ResourceOwnerInventoryReadValidation.Bound(result, Limits);
        return result;
    }
}
