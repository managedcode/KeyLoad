using System.Text;
using KeyLoad.Core;
using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Query.Features.Search;

namespace KeyLoad.Query;

/// <summary>Runs exact text, vector and hybrid ranking within one authorized read cut.</summary>
public sealed class SearchEngine(DatabaseEngine database, ITextProjection? textProjection = null)
{
    private const string InvalidSearch = "The search budgets or branch weights are invalid.";
    private const string InvalidText = "A bounded text query and field are required.";
    private const string InvalidVector = "A finite vector and a matching typed vector space are required.";
    private const string MissingSelectedDocument = "A selected search document is unavailable.";
    private const string ResultExceeded = "The search result byte budget is exceeded.";
    private const int MaxLimit = 1_000;
    private const int MaxTextBytes = 4_096;
    private const int MaxVectorDimension = 4_096;

    /// <summary>Returns exact fused ranks with selected documents projected by persisted policy.</summary>
    /// <param name="principalId">Persisted principal identity.</param>
    /// <param name="request">Search fields, query and result limit.</param>
    /// <param name="cancellationToken">Caller cancellation for the complete read.</param>
    /// <returns>Ranked and authorized documents.</returns>
    public RankedDocument[] Search(string principalId, SearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var budget = new ReadExecutionBudget(database.Limits, cancellationToken: cancellationToken);
        budget.Check();
        using var reservation = database.AdmitQuery(cancellationToken);
        Validate(request);
        var similarity = request.Vector is { } vector
            ? PreparedSimilarity.Create(vector.AsMemory(), request.Space!.Metric) : null;
        return database.WithQueryView(principalId, request.Partition, request.Collection, (view, principal, resource) =>
        {
            if (request.Text is not null)
            {
                database.Authorization.RequireFieldUse(principal, resource, request.TextField!);
            }
            if (similarity is not null)
            {
                database.Authorization.Require(principal, request.Partition, request.Collection, Capability.VectorSearch);
                database.Authorization.RequireFieldUse(principal, resource, request.VectorField!);
            }
            budget.Check();
            var fusion = new SearchRankFusion(request.FusionConstant, request.Limit, budget);
            if (request.Text is not null)
            {
                fusion.AddBranch(RankText(view, principal, resource, request, budget), request.TextWeight);
            }
            if (similarity is not null)
            {
                fusion.AddBranch(VectorRanker.Rank(database, view, principal, request, similarity, budget), request.VectorWeight);
            }
            return ProjectSelected(view, principal, resource, fusion.Select(), budget);
        });
    }

    private SearchScore[] RankText(KeyLoad.Storage.IKeyValueView view, PrincipalRecord principal,
        ResourceDefinition resource, SearchRequest request, ReadExecutionBudget budget)
    {
        var ranker = new TextRanker(request.Text!, request.TextField!, budget);
        if (!ranker.HasTerms)
        {
            return ranker.Rank();
        }

        var lease = textProjection?.Acquire(CreateTextProjectionScope(principal, resource, request), budget);
        Exception? primaryFailure = null;
        try
        {
            if (lease is not null)
            {
                ranker.AttachProjection(lease);
            }
            database.VisitVisibleDocuments(view, principal, request.Partition, request.Collection, budget, ranker.Visit);
            var scores = ranker.Rank();
            if (lease is not null)
            {
                var references = scores.Select(score => score.Reference).Distinct().ToArray();
                lease.VerifyCandidates(ranker.Terms, references, budget);
            }
            return scores;
        }
        catch (Exception error)
        {
            primaryFailure = error;
            throw;
        }
        finally
        {
            DisposeTextProjection(lease, primaryFailure);
        }
    }

    private TextProjectionScope CreateTextProjectionScope(PrincipalRecord principal, ResourceDefinition resource,
        SearchRequest request)
    {
        var identity = database.Store.Identity;
        return new(identity.NodeId, identity.Incarnation, identity.FormatVersion, identity.ReadGeneration,
            database.Store.Position, request.Partition, request.Collection, request.TextField!, principal.Id,
            principal.PolicyEpoch, resource.SchemaVersion);
    }

    private static void DisposeTextProjection(ITextProjectionLease? lease, Exception? primaryFailure)
    {
        if (lease is null)
        {
            return;
        }
        try
        {
            lease.Dispose();
        }
        catch (Exception cleanupFailure) when (primaryFailure is not null)
        {
            throw new AggregateException(primaryFailure, cleanupFailure);
        }
    }

    private RankedDocument[] ProjectSelected(KeyLoad.Storage.IKeyValueView view, PrincipalRecord principal,
        ResourceDefinition resource, SearchScore[] selected, ReadExecutionBudget budget)
    {
        var result = new RankedDocument[selected.Length];
        long projectedBytes = 0;
        for (var index = 0; index < selected.Length; index++)
        {
            budget.Check();
            var reference = selected[index].Reference;
            var document = budget.ReadRecord<DocumentRecord>(view, DocumentStorageKeys.RecordKey(reference))
                ?? throw Errors.Fail(ErrorCode.HistoryUnavailable, MissingSelectedDocument);
            var ranked = new RankedDocument(database.Project(principal, resource, document), selected[index].Score);
            var bytes = budget.MeasureResult(ranked);
            if (bytes > database.Limits.MaxBatchBytes - projectedBytes)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, ResultExceeded);
            }
            projectedBytes += bytes;
            result[index] = ranked;
        }
        budget.CheckResult(result);
        return result;
    }

    private void Validate(SearchRequest request)
    {
        if (request.Limit is < 1 or > MaxLimit || request.Limit > database.Limits.MaxResults || request.FusionConstant < 1
            || !double.IsFinite(request.TextWeight) || !double.IsFinite(request.VectorWeight)
            || request.TextWeight < 0 || request.VectorWeight < 0 || request.Text is null && request.Vector is null)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidSearch);
        }
        if (request.Text is { } text && (request.TextField is null || Encoding.UTF8.GetByteCount(text) > MaxTextBytes))
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidText);
        }
        if (request.Vector is { } vector && (vector.IsDefault || request.VectorField is null || request.Space is null
            || request.Space.Dimension is < 1 or > MaxVectorDimension || vector.Length != request.Space.Dimension
            || !Enum.IsDefined(request.Space.Metric)))
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidVector);
        }
    }

    /// <summary>Computes the selected vector metric using the same SIMD and scalar grouping as search.</summary>
    /// <param name="left">Finite query vector.</param>
    /// <param name="right">Finite candidate vector.</param>
    /// <param name="metric">Typed distance metric.</param>
    /// <returns>Similarity score; Euclidean distance is negated.</returns>
    public static double Similarity(float[] left, float[] right, DistanceMetric metric)
    {
        ArgumentNullException.ThrowIfNull(left);
        var prepared = PreparedSimilarity.Create(left.AsMemory(), metric);
        ArgumentNullException.ThrowIfNull(right);
        return prepared.Score(right.AsMemory());
    }
}
