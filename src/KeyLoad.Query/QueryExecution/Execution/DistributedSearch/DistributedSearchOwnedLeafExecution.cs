using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using Microsoft.Extensions.Options;

namespace KeyLoad.Query.Features.QueryExecution;

internal static partial class DistributedSearchOwnedLeafExecution
{
    private const string OwnerChanged = "The distributed search receiving owner changed.";
    private const string InvalidPhase = "The distributed search receiving phase is invalid.";

    internal static DistributedSearchLeafResultV1 ExecuteV2(DatabaseEngine database, string principalId,
        DistributedSearchOwnedLeafV2 request, PhysicalShardRecord actualOwner,
        IOptions<QueryExecutionOptions> options, TimeProvider clock, DateTimeOffset originalExpiry,
        CancellationToken token, IResourceLifetimeWorkBinding? binding = null)
    {
        if (request is null || request.Original is null || request.Definition is null || request.Coverage is null
            || request.Principal is null || request.Principal.Id != principalId)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, InvalidPhase);
        }
        var original = request.Original;
        var budget = new ReadExecutionBudget(database.OperationLimitsOptions, clock, token);
        if (binding is not null)
        {
            budget.AttachResourceLifetimeBinding(binding);
        }
        var execution = options.Value;
        execution.Validate();
        QueryResultBudgetPolicy.Constrain(budget, execution);
        budget.ConstrainLifetime(originalExpiry);
        DistributedSearchOwnedLeafValidation.Require(original, database.Limits, execution, budget);
        PartitionControlLifetimeCoverage.Require(request.Coverage, original.Search.Partition,
            [request.Definition], database.Limits.MaxBatchBytes);
        RequireOwner(database, original.Owner, actualOwner);
        budget.ConstrainResultBytes(original.MaxResultBytes);
        var grant = budget.CreateReadGrant(original.MaxReadBytes, original.MaxExaminedRecords);
        var result = ExecutePhase(database, principalId, original, options, budget, grant,
            new(request.Definition, request.Coverage, request.Principal));
        budget.CheckResult(result);
        return result;
    }

    private static DistributedSearchLeafResultV1 ExecutePhase(DatabaseEngine database, string principalId,
        DistributedSearchOwnedLeafV1 request, IOptions<QueryExecutionOptions> options,
        ReadExecutionBudget budget, ReadExecutionBudgetReadGrant grant, PartitionQueryLifetimeMetadata? metadata = null)
    {
        var witness = request.Witness;
        DistributedSearchCandidateLeafV1? candidates = null;
        DistributedSearchProjectionLeafV1? projection = null;
        switch (request.Phase)
        {
            case DistributedSearchPhase.Statistics:
                witness = DistributedTextStatisticsLeaf.Execute(database, principalId, request.Search,
                    request.Owner, request.Tenant, options, budget, grant, metadata);
                break;
            case DistributedSearchPhase.Candidates:
                candidates = DistributedSearchCandidateExecution.Execute(database, principalId, request.Search,
                    request.Tenant, witness!, request.Statistics!, request.Scope!, request.SourceWindowId!, options, budget, grant, metadata);
                break;
            case DistributedSearchPhase.Projection:
                projection = DistributedSearchProjectionExecution.Execute(database, principalId, request.Search,
                    request.Tenant, witness!, request.Selected, budget, grant, metadata);
                break;
            case DistributedSearchPhase.Revalidate:
                _ = DistributedSearchWitnessRevalidation.Execute(database, principalId, request.Search,
                    request.Tenant, witness!, budget, grant, metadata);
                break;
            default:
                throw Errors.Fail(ErrorCode.Corruption, InvalidPhase);
        }
        return new(request.Phase, witness!, candidates, projection, grant.ReadBytes, grant.ExaminedRecords);
    }

    private static void RequireOwner(DatabaseEngine database, PhysicalShardRecord expected, PhysicalShardRecord actual)
    {
        if (expected.PhysicalShardId != actual.PhysicalShardId || expected.Incarnation != actual.Incarnation
            || database.Store.Identity.Incarnation != actual.Incarnation)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, OwnerChanged);
        }
    }
}
