using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Core.DocumentStorage;
using KeyLoad.Query.Search;
using KeyLoad.Storage;

namespace KeyLoad.Query.QueryExecution;

internal static class DistributedSearchProjectionExecution
{
    private const string ChangedSelectedDocument = "The selected distributed search document changed.";
    private const string ReceivingPrincipalChanged = "The distributed search receiving principal changed.";

    internal static DistributedSearchProjectionLeaf Execute(DatabaseEngine database, string principalId,
        SearchRequest request, string tenant, DistributedTextWitness witness,
        ImmutableArray<GlobalBranchCandidate> selected, ReadExecutionBudget budget, PartitionQueryLifetimeMetadata? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(witness);
        ArgumentNullException.ThrowIfNull(budget);
        budget.Check();
        if (selected.IsDefault || selected.Length > request.Limit)
        {
            throw Errors.Fail(ErrorCode.Corruption, ChangedSelectedDocument);
        }
        using var admission = database.AdmitQuery(budget.Cancellation);
        Func<IKeyValueView, PrincipalRecord, ResourceDefinition, string, DistributedSearchProjectionLeaf> read =
            (view, principal, resource, digest) => Project(database, view, principal, resource, digest,
                request, tenant, witness, selected, budget, metadata);
        return metadata is null
            ? database.WithPartitionQueryFenceView(principalId, request.Partition, request.Collection, read, budget)
            : database.WithPartitionQueryLifetimeView(principalId, request.Partition, request.Collection, budget, metadata, read);
    }

    private static DistributedSearchProjectionLeaf Project(DatabaseEngine database, IKeyValueView view,
        PrincipalRecord principal, ResourceDefinition resource, string digest, SearchRequest request,
        string tenant, DistributedTextWitness witness, ImmutableArray<GlobalBranchCandidate> selected,
        ReadExecutionBudget budget, PartitionQueryLifetimeMetadata? metadata)
    {
        budget.Check();
        if (!string.Equals(principal.TenantId, tenant, StringComparison.Ordinal))
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, ReceivingPrincipalChanged);
        }
        DistributedTextWitnessValidation.Require(database, view, principal, resource, digest,
            witness, request.Partition, budget);
        DistributedSearchFieldAuthorization.Require(database, principal, resource, request, metadata);
        var hits = ImmutableArray.CreateBuilder<RankedDocument>(selected.Length);
        foreach (var candidate in selected)
        {
            budget.Check();
            GlobalBranchValidation.ValidateCandidate(candidate);
            if (candidate.Reference.Partition != request.Partition || candidate.Reference.Collection != request.Collection)
            {
                throw Errors.Fail(ErrorCode.Corruption, ChangedSelectedDocument);
            }
            var document = budget.ReadRecord<DocumentRecord>(view, DocumentStorageKeys.RecordKey(candidate.Reference));
            if (document is null || document.Deleted || document.Reference != candidate.Reference
                || document.Revision != candidate.Revision
                || !database.Authorization.CanReadRow(principal, document.Access)
                || metadata is not null && !database.Authorization.CanReadRow(metadata.Principal, document.Access))
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, ChangedSelectedDocument);
            }
            var projected = metadata is null ? database.Project(principal, resource, document)
                : database.ProjectWithAuthorityIntersection(metadata.Principal, metadata.Definition,
                    principal, resource, document).Value;
            hits.Add(new RankedDocument(projected, candidate.Score));
        }
        var result = new DistributedSearchProjectionLeaf(witness, hits.MoveToImmutable());
        return result;
    }
}
