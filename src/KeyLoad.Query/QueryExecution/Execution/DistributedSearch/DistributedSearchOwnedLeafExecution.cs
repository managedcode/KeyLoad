using KeyLoad.Core;
using KeyLoad.Core.ResourceExecution;
using Microsoft.Extensions.Options;

namespace KeyLoad.Query.QueryExecution;

internal static partial class DistributedSearchOwnedLeafExecution
{
    private const string OwnerChanged = "The distributed search receiving owner changed.";
    private const string InvalidPhase = "The distributed search receiving phase is invalid.";

    internal static DistributedSearchLeafResult Execute(DatabaseEngine database, string principalId,
        DistributedSearchOwnedLeaf request, PhysicalShardRecord actualOwner,
        IOptions<QueryExecutionOptions> options, TimeProvider clock, DateTimeOffset originalExpiry,
        CancellationToken token, IResourceLifetimeWorkBinding? binding = null)
    {
        if (request is null || request.Definition is null || request.Coverage is null
            || request.Principal is null || request.Principal.Id != principalId)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, InvalidPhase);
        }
        var original = request;
        var budget = new ReadExecutionBudget(clock, token);
        if (binding is not null)
        {
            budget.AttachResourceLifetimeBinding(binding);
        }
        var execution = options.Value;
        execution.Validate();
        budget.ConstrainLifetime(originalExpiry);
        DistributedSearchOwnedLeafValidation.Require(original, execution, budget);
        RequireOwner(database, original.Owner, actualOwner);
        var result = ExecutePhase(database, principalId, original, options, budget,
            new(request.Definition, request.Coverage, request.Principal));
        return result;
    }

    private static DistributedSearchLeafResult ExecutePhase(DatabaseEngine database, string principalId,
        DistributedSearchOwnedLeaf request, IOptions<QueryExecutionOptions> options,
        ReadExecutionBudget budget, PartitionQueryLifetimeMetadata? metadata = null)
    {
        var witness = request.Witness;
        DistributedSearchCandidateLeaf? candidates = null;
        DistributedSearchProjectionLeaf? projection = null;
        switch (request.Phase)
        {
            case DistributedSearchPhase.Statistics:
                witness = DistributedTextStatisticsLeaf.Execute(database, principalId, request.Search,
                    request.Owner, request.Tenant, options, budget, metadata);
                break;
            case DistributedSearchPhase.Candidates:
                candidates = DistributedSearchCandidateExecution.Execute(database, principalId, request.Search,
                    request.Tenant, witness!, request.Statistics!, request.Scope!, request.SourceWindowId!, options, budget, metadata);
                break;
            case DistributedSearchPhase.Projection:
                projection = DistributedSearchProjectionExecution.Execute(database, principalId, request.Search,
                    request.Tenant, witness!, request.Selected, budget, metadata);
                break;
            case DistributedSearchPhase.Revalidate:
                _ = DistributedSearchWitnessRevalidation.Execute(database, principalId, request.Search,
                    request.Tenant, witness!, budget, metadata);
                break;
            default:
                throw Errors.Fail(ErrorCode.Corruption, InvalidPhase);
        }
        return new(request.Phase, witness!, candidates, projection);
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
