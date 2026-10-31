using System.Runtime.InteropServices;
using KeyLoad.Core;
using KeyLoad.Core.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Query.Search;

internal static class AnnPublicSearchExecutor
{
    private const string Missing = "The execution ANN dependency history is unavailable; explicitly rebuild the generation.";

    internal static Task<AnnSearchPage> ExecuteAsync(DatabaseEngine database, IAnnProjection? projection,
        string principalId, ApproximateSearchRequest request, IOptions<QueryExecutionOptions> configured,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var execution = configured.Value;
        if (!execution.EnableApproximateSearch)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, AnnSearchProtocol.Disabled);
        }
        var budget = new ReadExecutionBudget(database.EvaluationClock, cancellationToken);
        budget.Check();
        using var reservation = database.AdmitQuery(cancellationToken);
        return Task.FromResult(Execute(database, projection, principalId, request, budget, execution));
    }

    internal static Task<AnnSearchPage> ExecuteAsync(DatabaseEngine database, IAnnProjection? projection,
        string principalId, ApproximateSearchRequest request, IOptions<QueryExecutionOptions> configured,
        ReadExecutionBudget budget, CancellationToken cancellationToken, AnnReadLifetimeMetadata? sourceMetadata = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(budget);
        var execution = configured.Value;
        if (!execution.EnableApproximateSearch)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, AnnSearchProtocol.Disabled);
        }
        budget.Check();
        using var reservation = database.AdmitQuery(cancellationToken);
        return Task.FromResult(Execute(database, projection, principalId, request, budget, execution, sourceMetadata));
    }

    internal static AnnSearchPage Execute(DatabaseEngine database, IAnnProjection? projection,
        string principalId, ApproximateSearchRequest request, ReadExecutionBudget budget,
        QueryExecutionOptions execution, AnnReadLifetimeMetadata? sourceMetadata = null)
    {
        AnnPublicRequestValidation.Validate(request, execution, budget);
        var search = request.Search;
        return database.WithQueryView(principalId, search.Partition, search.Collection, Capability.VectorSearch, (view, principal, resource) =>
        {
            database.Authorization.Require(principal, search.Partition, search.Collection, Capability.VectorSearch);
            if (sourceMetadata is not null)
            {
                AnnLifetimeReadAuthority.Require(database, view, principal, resource, search.Partition,
                    search.Collection, search.VectorField!, sourceMetadata, budget);
            }
            else
            {
                database.Authorization.RequireFieldUse(principal, resource, search.VectorField!);
            }
            var owner = projection ?? throw Errors.Fail(ErrorCode.HistoryUnavailable, Missing);
            var lease = owner.Acquire(view, new(search.Partition, search.Collection, search.VectorField!,
                search.Space!, request.Consumer, request.IndexGeneration), budget);
            Exception? primary = null;
            try
            {
                var caller = AnnSeedCollector.CaptureView(database, view, principalId,
                    search.Partition, search.Collection, search.VectorField!, search.Space!,
                    lease.CallerSeedOptions, budget, scopeWork: lease.ScopeWork, sourceMetadata: sourceMetadata);
                var work = new AnnWorkBudget(budget, lease.CallerSeedOptions.Value.MaxWorkUnits);
                work.Charge(lease.ScopeWork.Units);
                var eligible = AnnPublicEligibility.Create(lease.Records, caller, search.AllowedIds, work);
                var result = lease.Search(search.Vector!.Value.AsMemory(), search.Limit, eligible, work);
                var scores = AnnPublicCandidateValidation.Scores(search, caller, lease.Records, eligible, result.Candidates, work);
                var fusion = new SearchRankFusion(search.FusionConstant, search.Limit, budget);
                fusion.AddBranch(scores, search.VectorWeight, SearchBranchKind.Vector);
                var selected = fusion.Select();
                var documents = sourceMetadata is null
                    ? SearchBranchExecution.ProjectSelected(database, view, principal, resource, selected, budget)
                    : AnnLifetimeProjection.ProjectSelected(database, view, principal, resource, sourceMetadata, selected, budget);
                var page = new AnnSearchPage(AnnPublicRequestValidation.CurrentVersion, ImmutableCollectionsMarshal.AsImmutableArray(documents),
                    caller.Cut.Position, Mode(result.Mode), result.Mode != AnnSearchMode.Approximate,
                    request.IndexGeneration, request.RequestedMode);
                return page;
            }
            catch (Exception error) { primary = error; throw; }
            finally { Dispose(lease, primary); }
        }, budget);
    }

    private static AnnPageMode Mode(AnnSearchMode mode) => mode switch
    {
        AnnSearchMode.ExactSmallSet => AnnPageMode.Exact,
        AnnSearchMode.Approximate => AnnPageMode.Approximate,
        _ => throw Errors.Fail(ErrorCode.Corruption, Missing)
    };

    private static void Dispose(IAnnProjectionLease lease, Exception? primary)
    {
        try
        {
            lease.Dispose();
        }
        catch (Exception cleanup)
        {
            if (primary is not null)
            {
                throw new AggregateException(primary, cleanup);
            }
            throw;
        }
    }
}
