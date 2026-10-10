using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using Microsoft.Extensions.Options;

namespace KeyLoad.Query.Features.QueryExecution;

internal static class DistributedSearchOwnedLeafExecution
{
    private const string OwnerChanged = "The distributed search receiving owner changed.";
    private const string InvalidPhase = "The distributed search receiving phase is invalid.";

    internal static DistributedSearchLeafResultV1 Execute(DatabaseEngine database, string principalId,
        DistributedSearchOwnedLeafV1 request, PhysicalShardRecord actualOwner,
        IOptions<QueryExecutionOptions> options, TimeProvider clock, DateTimeOffset originalExpiry,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(actualOwner);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        var budget = new ReadExecutionBudget(database.OperationLimitsOptions, clock, token);
        var execution = options.Value;
        execution.Validate();
        QueryResultBudgetPolicy.Constrain(budget, execution);
        budget.ConstrainLifetime(originalExpiry);
        DistributedSearchOwnedLeafValidation.Require(request, database.Limits, execution, budget);
        RequireOwner(database, request.Owner, actualOwner);
        budget.ConstrainResultBytes(request.MaxResultBytes);
        var grant = budget.CreateReadGrant(request.MaxReadBytes, request.MaxExaminedRecords);
        var result = ExecutePhase(database, principalId, request, options, budget, grant);
        budget.CheckResult(result);
        return result;
    }

    private static DistributedSearchLeafResultV1 ExecutePhase(DatabaseEngine database, string principalId,
        DistributedSearchOwnedLeafV1 request, IOptions<QueryExecutionOptions> options,
        ReadExecutionBudget budget, ReadExecutionBudgetReadGrant grant)
    {
        var witness = request.Witness;
        DistributedSearchCandidateLeafV1? candidates = null;
        DistributedSearchProjectionLeafV1? projection = null;
        switch (request.Phase)
        {
            case DistributedSearchPhase.Statistics:
                witness = DistributedTextStatisticsLeaf.Execute(database, principalId, request.Search,
                    request.Owner, request.Tenant, options, budget, grant);
                break;
            case DistributedSearchPhase.Candidates:
                candidates = DistributedSearchCandidateExecution.Execute(database, principalId, request.Search,
                    request.Tenant, witness!, request.Statistics!, request.Scope!, request.SourceWindowId!, options, budget, grant);
                break;
            case DistributedSearchPhase.Projection:
                projection = DistributedSearchProjectionExecution.Execute(database, principalId, request.Search,
                    request.Tenant, witness!, request.Selected, budget, grant);
                break;
            case DistributedSearchPhase.Revalidate:
                _ = DistributedSearchWitnessRevalidation.Execute(database, principalId, request.Search,
                    request.Tenant, witness!, budget, grant);
                break;
            default:
                throw Errors.Fail(ErrorCode.Corruption, InvalidPhase);
        }
        return new(request.Phase, witness!, candidates, projection, grant.ReadBytes, grant.ExaminedRecords);
    }

    private static void RequireOwner(DatabaseEngine database, PhysicalShardRecord expected, PhysicalShardRecord actual)
    {
        if (expected.PhysicalShardId != actual.PhysicalShardId || expected.Incarnation != actual.Incarnation
            || expected.PlacementEpoch != actual.PlacementEpoch
            || !expected.VoterIds.SequenceEqual(actual.VoterIds, StringComparer.Ordinal)
            || database.Store.Identity.Incarnation != actual.Incarnation)
        { throw Errors.Fail(ErrorCode.OwnershipLost, OwnerChanged); }
    }
}
