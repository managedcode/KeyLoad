using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Query.Features.Search;
using KeyLoad.Storage;

namespace KeyLoad.Query.Features.QueryExecution;

internal static class DistributedSearchProjectionExecution
{
    private const string ChangedSelectedDocument = "The selected distributed search document changed.";
    private const string ReceivingPrincipalChanged = "The distributed search receiving principal changed.";
    private const long NoResultBytes = 0;

    internal static DistributedSearchProjectionLeaf Execute(DatabaseEngine database, string principalId,
        SearchRequest request, string tenant, DistributedTextWitness witness,
        ImmutableArray<GlobalBranchCandidate> selected, ReadExecutionBudget budget, ReadExecutionBudgetReadGrant grant, PartitionQueryLifetimeMetadata? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(witness);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(grant);
        budget.Check();
        if (selected.IsDefault || selected.Length > request.Limit)
        {
            throw Errors.Fail(ErrorCode.Corruption, ChangedSelectedDocument);
        }
        using var admission = database.AdmitQuery(budget.Cancellation);
        using var charged = budget.EnterReadGrant(grant);
        Func<IKeyValueView, PrincipalRecord, ResourceDefinition, string, DistributedSearchProjectionLeaf> read =
            (view, principal, resource, digest) => Project(database, view, principal, resource, digest,
                request, tenant, witness, selected, budget, grant, metadata);
        return metadata is null
            ? database.WithPartitionQueryFenceView(principalId, request.Partition, request.Collection, grant, read, budget)
            : database.WithPartitionQueryLifetimeView(principalId, request.Partition, request.Collection,
                grant, budget, metadata, read);
    }

    private static DistributedSearchProjectionLeaf Project(DatabaseEngine database, IKeyValueView view,
        PrincipalRecord principal, ResourceDefinition resource, string digest, SearchRequest request,
        string tenant, DistributedTextWitness witness, ImmutableArray<GlobalBranchCandidate> selected,
        ReadExecutionBudget budget, ReadExecutionBudgetReadGrant grant, PartitionQueryLifetimeMetadata? metadata)
    {
        budget.Check();
        if (!string.Equals(principal.TenantId, tenant, StringComparison.Ordinal))
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, ReceivingPrincipalChanged);
        }
        DistributedTextWitnessValidation.Require(database, view, principal, resource, digest,
            witness, request.Partition, grant, budget);
        DistributedSearchFieldAuthorization.Require(database, principal, resource, request, metadata);
        var admission = new GlobalBranchByteAdmission(budget.MaximumResultBytes, budget);
        admission.Accept(PartitionQueryRetention.CandidateArrayBytes(selected.Length));
        var hits = ImmutableArray.CreateBuilder<RankedDocument>(selected.Length);
        var projectedBytes = NoResultBytes;
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
            var hit = new RankedDocument(projected, candidate.Score);
            var bytes = budget.MeasureResult(hit);
            if (bytes > budget.MaximumResultBytes - projectedBytes)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, ChangedSelectedDocument);
            }
            projectedBytes += bytes;
            hits.Add(hit);
        }
        var result = new DistributedSearchProjectionLeaf(witness, hits.MoveToImmutable(), grant.ReadBytes, grant.ExaminedRecords);
        budget.CheckResult(result);
        return result;
    }
}
