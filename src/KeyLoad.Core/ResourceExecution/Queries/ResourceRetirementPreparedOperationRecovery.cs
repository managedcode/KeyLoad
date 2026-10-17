using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal ResourceRetirementPreparedOperationV1 ReadPreparedResourceRetirementOperation(string principalId,
        ResourceRetirementAttemptContextV1 context, ResourceRetirementAttemptV1 attempt, ReadExecutionBudget work)
    {
        work.Check();
        var result = Store.Read(raw =>
        {
            var view = work.CreateView(raw);
            RequireInventoryAdministrator(view, principalId);
            ResourceRetirementAttemptContextValidation.Require(context, Limits);
            ResourceRetirementAttemptValidation.Witness(ResourceRetirementPreparedOperationStorage.Prepared(attempt), context, Limits);
            var owner = ReadInventoryPhysicalCatalog(view).DefaultShard;
            if (!KeyLoad.Core.Features.ClusterRouting.Validation.PhysicalOwnerEntryValidation.SameOwner(owner,
                ResourceRetirementAttemptContextValidation.ExecutionOwner(context)))
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, ResourceRetirementProtocol.Changed);
            }
            var prepared = ResourceRetirementPreparedOperationStorage.Read(view, context, attempt, Limits)
                ?? throw Errors.Fail(context.Purpose == ResourceRetirementAttemptPurpose.CleanupPage
                    ? ErrorCode.NotFound : ErrorCode.RecoveryRequired, ResourceRetirementProtocol.Missing);
            RequirePreparedResourceRetirementOriginal(prepared);
            return prepared;
        });
        work.CheckResult(result);
        work.Check();
        return result;
    }
}
