using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Query.Features.Search;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Query.Features.QueryExecution;

internal static class DistributedSearchCandidateExecution
{
    private const string ReceivingPrincipalChanged = "The distributed search receiving principal changed.";

    internal static DistributedSearchCandidateLeafV1 Execute(DatabaseEngine database, string principalId,
        SearchRequest request, string tenant, DistributedTextWitnessV1 witness,
        DistributedTextStatisticsV1 statistics, GlobalBranchScope scope, string sourceWindowId,
        IOptions<QueryExecutionOptions> options, ReadExecutionBudget budget, ReadExecutionBudgetReadGrant grant)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(witness);
        ArgumentNullException.ThrowIfNull(statistics);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(grant);
        budget.Check();
        var execution = options.Value;
        execution.Validate();
        SearchRequestValidation.Validate(request, database.Limits, false, execution);
        FilteredSearchEligibility.ValidateRequest(request.AllowedIds, database.Limits, budget);
        var eligibility = FilteredSearchEligibility.Create(request.AllowedIds, budget);
        using var admission = database.AdmitQuery(budget.Cancellation);
        using var charged = budget.EnterReadGrant(grant);
        return database.WithPartitionQueryFenceView(principalId, request.Partition, request.Collection, grant,
            (view, principal, resource, digest) => Capture(database, view, principal, resource, digest,
                request, tenant, witness, statistics, scope, sourceWindowId, execution, eligibility, budget, grant));
    }

    private static DistributedSearchCandidateLeafV1 Capture(DatabaseEngine database, IKeyValueView view,
        PrincipalRecord principal, ResourceDefinition resource, string digest, SearchRequest request,
        string tenant, DistributedTextWitnessV1 witness, DistributedTextStatisticsV1 statistics,
        GlobalBranchScope scope, string sourceWindowId, QueryExecutionOptions execution,
        FilteredSearchEligibility eligibility, ReadExecutionBudget budget, ReadExecutionBudgetReadGrant grant)
    {
        budget.Check();
        if (!string.Equals(principal.TenantId, tenant, StringComparison.Ordinal))
        { throw Errors.Fail(ErrorCode.PermissionDenied, ReceivingPrincipalChanged); }
        DistributedTextWitnessValidation.Require(database, view, principal, resource, digest,
            witness, request.Partition, grant, budget);
        GlobalBranchWindow? text = null;
        GlobalBranchWindow? vector = null;
        if (request.Text is not null)
        {
            database.Authorization.RequireFieldUse(principal, resource, request.TextField!);
            text = DistributedSearchCanonicalText.Capture(database, view, principal, request,
                statistics, scope, sourceWindowId, execution, eligibility, budget);
        }
        if (request.Vector is { } query)
        {
            database.Authorization.Require(principal, request.Partition, request.Collection, Capability.VectorSearch);
            database.Authorization.RequireFieldUse(principal, resource, request.VectorField!);
            var similarity = PreparedSimilarity.Create(query.AsMemory(), request.Space!.Metric);
            var scores = VectorRanker.Rank(database, view, principal, request, similarity, budget, eligibility);
            vector = DistributedSearchCandidateWindow.Capture(view, scores, request.Partition, request.Collection,
                DistributedSearchBranchNames.Vector, GlobalBranchKind.Vector, sourceWindowId, scope, budget);
        }
        var result = new DistributedSearchCandidateLeafV1(witness, text, vector, grant.ReadBytes, grant.ExaminedRecords);
        budget.CheckResult(result);
        return result;
    }
}
