using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal (ResourceRetirementDropParentV1? Parent, ResourceRetirementRequestV1? Admitted)
        ReadResourceRetirementDropParentOrAdmit(string principalId, ResourceRetirementDropParentLookupV1 lookup,
            DateTimeOffset expiresAt, ReadExecutionBudget work)
    {
        work.Check();
        var result = Store.Read(raw =>
        {
            var view = work.CreateView(raw);
            var principal = RequireInventoryAdministrator(view, principalId);
            ResourceRetirementDropParentStorage.RequireLookup(lookup, Limits);
            var parent = ReadRetirementDropParent(view, lookup, principal.Id);
            if (parent is not null)
            {
                return (Parent: parent, Admitted: (ResourceRetirementRequestV1?)null);
            }
            ResourceOwnerInventoryReadValidation.Expiry(expiresAt, EvaluationClock.GetUtcNow(), work);
            var request = lookup.Request;
            var (definition, lifetime) = ReadInventoryResourcePair(view, request.TenantId,
                request.DatabaseId, request.ResourceName, initial: true);
            if (definition.Kind != ResourceKind.Collection || definition.RelationalSchema is null)
            {
                throw Errors.Fail(ErrorCode.Validation, ResourceRetirementProtocol.Invalid);
            }
            ResourceRetirementStorage.RequireOpen(view, lifetime, Limits);
            var admitted = new ResourceRetirementRequestV1(ResourceRetirementProtocol.Version, lookup.CommandId,
                request.TenantId, request.DatabaseId, request.ResourceName, lifetime.Incarnation);
            ResourceRetirementValidation.Request(admitted, Limits);
            return (Parent: (ResourceRetirementDropParentV1?)null, Admitted: admitted);
        });
        if (result.Parent is not null)
        {
            work.CheckResult(result.Parent);
        }
        if (result.Admitted is not null)
        {
            work.CheckResult(result.Admitted);
        }
        work.Check();
        return result;
    }
}
