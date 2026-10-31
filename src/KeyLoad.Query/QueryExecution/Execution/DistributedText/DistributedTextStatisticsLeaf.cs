using KeyLoad.Core;
using KeyLoad.Query.Search;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Query.QueryExecution;

internal static class DistributedTextStatisticsLeaf
{
    private const string CanonicalTextRequired = "The distributed statistics require the canonical text profile.";
    private const string ReceivingPrincipalChanged = "The distributed statistics receiving principal changed.";

    internal static DistributedTextWitness Execute(DatabaseEngine database, string principalId,
        SearchRequest request, PhysicalShardRecord owner, string tenant,
        IOptions<QueryExecutionOptions> options, ReadExecutionBudget budget, PartitionQueryLifetimeMetadata? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(budget);
        budget.Check();
        var execution = options.Value;
        execution.Validate();
        SearchRequestValidation.Validate(request, false, execution);
        if (request.TextIndex is not null)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, CanonicalTextRequired);
        }
        using var admission = database.AdmitQuery(budget.Cancellation);
        Func<IKeyValueView, PrincipalRecord, ResourceDefinition, string, DistributedTextWitness> read =
            (view, principal, resource, digest) => Capture(database, view, principal, resource, digest,
                request, owner, tenant, execution, budget, metadata);
        return metadata is null
            ? database.WithPartitionQueryFenceView(principalId, request.Partition, request.Collection, read, budget)
            : database.WithPartitionQueryLifetimeView(principalId, request.Partition, request.Collection, budget, metadata, read);
    }

    private static DistributedTextWitness Capture(DatabaseEngine database, IKeyValueView view,
        PrincipalRecord principal, ResourceDefinition resource, string digest, SearchRequest request,
        PhysicalShardRecord owner, string tenant, QueryExecutionOptions execution,
        ReadExecutionBudget budget, PartitionQueryLifetimeMetadata? metadata)
    {
        budget.Check();
        if (!string.Equals(principal.TenantId, tenant, StringComparison.Ordinal))
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, ReceivingPrincipalChanged);
        }
        DistributedSearchFieldAuthorization.Require(database, principal, resource, request, metadata);
        var placement = database.ReadAtomicPartitionPlacementForAuthorizedQuery(view, request.Partition);
        PartitionQueryPlacementValidation.Validate(placement, request.Partition, owner);
        if (request.Text is not null)
        {
            database.Authorization.RequireFieldUse(principal, resource, request.TextField!);
        }
        if (request.Vector is not null)
        {
            database.Authorization.Require(principal, request.Partition, request.Collection, Capability.VectorSearch);
            database.Authorization.RequireFieldUse(principal, resource, request.VectorField!);
        }
        var statistics = CaptureStatistics(database, view, principal, request, execution, budget, metadata?.Principal);
        var identity = database.Store.Identity;
        var result = new DistributedTextWitness(request.Partition, owner, identity.NodeId,
            identity.ReadGeneration, database.Store.Position, principal.PolicyEpoch, resource.SchemaVersion,
            digest, statistics, identity.Incarnation);
        return result;
    }

    private static DistributedTextStatistics CaptureStatistics(DatabaseEngine database, IKeyValueView view,
        PrincipalRecord principal, SearchRequest request, QueryExecutionOptions execution, ReadExecutionBudget budget,
        PrincipalRecord? sourcePrincipal)
    {
        if (request.Text is not null)
        {
            var ranker = TextRanker.ForStatistics(request.Text, request.TextField!, budget,
                execution.TextBudgetCheckInterval, execution.MaximumDocumentWords, execution.MaximumWordCharacters);
            database.VisitVisibleDocuments(view, principal, request.Partition, request.Collection, budget, document =>
            {
                if (sourcePrincipal is null || database.Authorization.CanReadRow(sourcePrincipal, document.Access))
                {
                    ranker.Visit(document);
                }
            });
            return ranker.CaptureStatistics();
        }
        var documents = 0;
        database.VisitVisibleDocuments(view, principal, request.Partition, request.Collection, budget,
            document =>
            {
                budget.Check();
                if (sourcePrincipal is null || database.Authorization.CanReadRow(sourcePrincipal, document.Access))
                {
                    documents++;
                }
            });
        return DistributedTextStatisticsCapture.Copy([], documents, 0, [], budget);
    }
}
