using KeyLoad.Core;
using KeyLoad.Query.Features.Search;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Query.Features.QueryExecution;

internal static class DistributedTextStatisticsLeaf
{
    private const int NoTextLength = 0;
    private const string CanonicalTextRequired = "The distributed statistics require the canonical text profile.";
    private const string ReceivingPrincipalChanged = "The distributed statistics receiving principal changed.";

    internal static DistributedTextWitnessV1 Execute(DatabaseEngine database, string principalId,
        SearchRequest request, PhysicalShardRecord owner, string tenant,
        IOptions<QueryExecutionOptions> options, ReadExecutionBudget budget, global::KeyLoad.Core.Features.ResourceExecution.Execution.ReadExecutionBudgetReadGrant grant)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(grant);
        budget.Check();
        var execution = options.Value;
        execution.Validate();
        SearchRequestValidation.Validate(request, database.Limits, false, execution);
        if (request.TextIndex is not null)
        { throw Errors.Fail(ErrorCode.UnsupportedCapability, CanonicalTextRequired); }
        using var admission = database.AdmitQuery(budget.Cancellation);
        using var charged = budget.EnterReadGrant(grant);
        return database.WithPartitionQueryFenceView(principalId, request.Partition, request.Collection, grant,
            (view, principal, resource, digest) => Capture(database, view, principal, resource, digest,
                request, owner, tenant, execution, budget, grant));
    }

    private static DistributedTextWitnessV1 Capture(DatabaseEngine database, IKeyValueView view,
        PrincipalRecord principal, ResourceDefinition resource, string digest, SearchRequest request,
        PhysicalShardRecord owner, string tenant, QueryExecutionOptions execution,
        ReadExecutionBudget budget, global::KeyLoad.Core.Features.ResourceExecution.Execution.ReadExecutionBudgetReadGrant grant)
    {
        budget.Check();
        if (!string.Equals(principal.TenantId, tenant, StringComparison.Ordinal))
        { throw Errors.Fail(ErrorCode.PermissionDenied, ReceivingPrincipalChanged); }
        var placement = DatabaseEngine.ReadAtomicPartitionPlacementForAuthorizedQuery(view, request.Partition, grant);
        PartitionQueryPlacementValidation.Validate(placement, request.Partition, owner);
        if (request.Text is not null)
        { database.Authorization.RequireFieldUse(principal, resource, request.TextField!); }
        if (request.Vector is not null)
        {
            database.Authorization.Require(principal, request.Partition, request.Collection, Capability.VectorSearch);
            database.Authorization.RequireFieldUse(principal, resource, request.VectorField!);
        }
        var statistics = CaptureStatistics(database, view, principal, request, execution, budget);
        var identity = database.Store.Identity;
        var result = new DistributedTextWitnessV1(request.Partition, owner, identity.NodeId,
            identity.ReadGeneration, database.Store.Position, principal.PolicyEpoch, resource.SchemaVersion,
            digest, statistics, grant.ReadBytes, grant.ExaminedRecords, identity.Incarnation);
        budget.CheckResult(result);
        return result;
    }

    private static DistributedTextStatisticsV1 CaptureStatistics(DatabaseEngine database, IKeyValueView view,
        PrincipalRecord principal, SearchRequest request, QueryExecutionOptions execution, ReadExecutionBudget budget)
    {
        if (request.Text is not null)
        {
            var ranker = TextRanker.ForStatistics(request.Text, request.TextField!, budget,
                execution.TextBudgetCheckInterval, execution.MaximumDocumentWords, execution.MaximumWordCharacters);
            database.VisitVisibleDocuments(view, principal, request.Partition, request.Collection, budget, ranker.Visit);
            return ranker.CaptureStatistics();
        }
        var documents = NoTextLength;
        database.VisitVisibleDocuments(view, principal, request.Partition, request.Collection, budget,
            _ => { budget.Check(); documents++; });
        return DistributedTextStatisticsCapture.Copy([], documents, NoTextLength, [], budget);
    }
}
