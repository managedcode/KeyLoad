using KeyLoad.Query;
using KeyLoad.Query.Search;
using KeyLoad.Server;
using KeyLoad.Server.Search;

namespace KeyLoad.UnitTests.Search;

internal sealed class TextCapturedCommitAuthorityHarness : IDisposable
{
    private const string ProjectionDirectory = "captured-commit-authority-text";
    private const string MissingOriginal = "The original captured execution search is unavailable.";
    private const string InvalidRequest = "The captured commit authority trial requires one execution text branch.";
    private readonly DatabaseEngine database;
    private readonly SearchRequest request;
    private readonly ReadExecutionBudget budget;
    private readonly QueryExecutionOptions execution;
    private readonly FilteredSearchEligibility eligibility;
    private readonly RuntimeTextProjection? projection;
    private ICapturedTextRead? captured;
    private SearchStart? original;
    private IDisposable? admission;
    private bool completionAttempted;
    private bool disposed;

    internal TextCapturedCommitAuthorityHarness(TestDatabase fixture, string principalId,
        SearchRequest request, CancellationToken token)
    {
        this.request = request;
        database = KeyLoad.UnitTests.Messaging.QueueWholeFlowStorage.Open(fixture.Store);
        var options = UnitExecutionOptions.QueryExecution();
        execution = options.Value;
        execution.Validate();
        budget = new(UnitExecutionOptions.DatabaseLimits(database.Limits), database.EvaluationClock, token);
        QueryResultBudgetPolicy.Constrain(budget, execution);
        budget.Check();
        SearchRequestValidation.Validate(request, database.Limits, false, execution);
        if (request.Text is null || request.TextField is null || request.Vector is not null)
        {
            throw new InvalidOperationException(InvalidRequest);
        }
        FilteredSearchEligibility.ValidateRequest(request.AllowedIds, database.Limits, budget);
        FilteredSearchRequestSizer.EnsureBounded(request, database.Limits.MaxQueryBytes, budget);
        eligibility = FilteredSearchEligibility.Create(request.AllowedIds, budget);
        try
        {
            projection = new(Path.Combine(fixture.Directory, ProjectionDirectory),
                UnitExecutionOptions.DatabaseLimits(database.Limits), fixture.Store.Identity.NodeId,
                UnitTextOptions.Execution());
            Search = new(database, options, projection);
            Capture(fixture, principalId);
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            Settle(failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    internal SearchEngine Search
    {
        get;
    } = null!;
    internal long CapturedPosition
    {
        get; private set;
    }
    internal long CapturedReadGeneration
    {
        get; private set;
    }

    private void Capture(TestDatabase fixture, string principalId)
    {
        admission = database.AdmitQuery(budget.Cancellation);
        captured = database.WithQueryView(principalId, request.Partition, request.Collection,
            (view, principal, resource) =>
            {
                database.Authorization.RequireFieldUse(principal, resource, request.TextField!);
                CapturedPosition = fixture.Store.Position;
                CapturedReadGeneration = fixture.Store.Identity.ReadGeneration;
                return TextCapturedReadFactory.Capture(database, projection!, view,
                    principal, resource, request, budget);
            });
        var ranker = new TextRanker(request.Text!, request.TextField!, budget, execution.TextBudgetCheckInterval,
            execution.MaximumDocumentWords, execution.MaximumWordCharacters);
        original = new(captured, ranker, null);
    }

    internal RankedDocument[] Complete()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (completionAttempted || original is null)
        {
            throw new InvalidOperationException(MissingOriginal);
        }
        completionAttempted = true;
        RankedDocument[]? result = null;
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() => result = SearchCapturedExecution.Complete(
            database, original, request, budget, null, eligibility), failures);
        ReleaseAdmission(failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return result!;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        var failures = new List<Exception>();
        Settle(failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private void Settle(List<Exception> failures)
    {
        try
        {
            captured?.Dispose();
        }
        catch (Exception error) when (KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        catch (Exception error) when (!KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        try
        {
            projection?.Dispose();
        }
        catch (Exception error) when (KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        catch (Exception error) when (!KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        ReleaseAdmission(failures);
    }

    private void ReleaseAdmission(List<Exception> failures)
    {
        try
        {
            admission?.Dispose();
        }
        catch (Exception error) when (KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        catch (Exception error) when (!KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        admission = null;
    }
}
