using KeyLoad.Core;
using KeyLoad.Query.Search;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Query.QueryExecution;

internal static class DistributedSearchCandidateExecution
{
    private const string ReceivingPrincipalChanged = "The distributed search receiving principal changed.";

    internal static DistributedSearchCandidateLeaf Execute(DatabaseEngine database, string principalId,
        SearchRequest request, string tenant, DistributedTextWitness witness,
        DistributedTextStatistics statistics, GlobalBranchScope scope, string sourceWindowId,
        IOptions<QueryExecutionOptions> options, ReadExecutionBudget budget, PartitionQueryLifetimeMetadata? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(database) /* KLD0041: BCL null validation accepts the database DatabaseEngine reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(request) /* KLD0041: BCL null validation accepts the request SearchRequest reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(witness) /* KLD0041: BCL null validation accepts the witness DistributedTextWitness reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(statistics) /* KLD0041: BCL null validation accepts the statistics DistributedTextStatistics reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(scope) /* KLD0041: BCL null validation accepts the scope GlobalBranchScope reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(options) /* KLD0041: BCL null validation accepts the options IOptions reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(budget) /* KLD0041: BCL null validation accepts the budget ReadExecutionBudget reference through its object parameter. */;
        budget.Check();
        var execution = options.Value;
        execution.Validate();
        SearchRequestValidation.Validate(request, false, execution);
        FilteredSearchEligibility.ValidateRequest(request.AllowedIds, budget);
        var eligibility = FilteredSearchEligibility.Create(request.AllowedIds, budget);
        using var admission = database.AdmitQuery(budget.Cancellation);
        Func<IKeyValueView, PrincipalRecord, ResourceDefinition, string, DistributedSearchCandidateLeaf> read =
            (view, principal, resource, digest) => Capture(database, view, principal, resource, digest,
                request, tenant, witness, statistics, scope, sourceWindowId, execution, eligibility, budget, metadata);
        return metadata is null
            ? database.WithPartitionQueryFenceView(principalId, request.Partition, request.Collection, read, budget)
            : database.WithPartitionQueryLifetimeView(principalId, request.Partition, request.Collection, budget, metadata, read);
    }

    private static DistributedSearchCandidateLeaf Capture(DatabaseEngine database, IKeyValueView view,
        PrincipalRecord principal, ResourceDefinition resource, string digest, SearchRequest request,
        string tenant, DistributedTextWitness witness, DistributedTextStatistics statistics,
        GlobalBranchScope scope, string sourceWindowId, QueryExecutionOptions execution,
        FilteredSearchEligibility eligibility, ReadExecutionBudget budget,
        PartitionQueryLifetimeMetadata? metadata)
    {
        budget.Check();
        if (!string.Equals(principal.TenantId, tenant, StringComparison.Ordinal))
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, ReceivingPrincipalChanged);
        }
        DistributedSearchFieldAuthorization.Require(database, principal, resource, request, metadata);
        DistributedTextWitnessValidation.Require(database, view, principal, resource, digest,
            witness, request.Partition, budget);
        GlobalBranchWindow? text = null;
        GlobalBranchWindow? vector = null;
        if (request.Text is not null)
        {
            database.Authorization.RequireFieldUse(principal, resource, request.TextField!);
            text = DistributedSearchCanonicalText.Capture(database, view, principal, request,
                statistics, scope, sourceWindowId, execution, eligibility, budget, metadata?.Principal);
        }
        if (request.Vector is { } query)
        {
            database.Authorization.Require(principal, request.Partition, request.Collection, Capability.VectorSearch);
            database.Authorization.RequireFieldUse(principal, resource, request.VectorField!);
            var similarity = PreparedSimilarity.Create(query.AsMemory(), request.Space!.Metric);
            var scores = VectorRanker.Rank(database, view, principal, request, similarity, budget, eligibility, metadata?.Principal);
            vector = DistributedSearchCandidateWindow.Capture(view, scores, request.Partition, request.Collection,
                DistributedSearchBranchNames.Vector, GlobalBranchKind.Vector, sourceWindowId, scope, budget);
        }
        var result = new DistributedSearchCandidateLeaf(witness, text, vector);
        return result;
    }
}
