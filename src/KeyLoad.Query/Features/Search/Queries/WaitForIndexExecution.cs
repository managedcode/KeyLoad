using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Query.Features.Search;

namespace KeyLoad.Query;

public sealed partial class SearchEngine
{
    private const string WaitTokenInvalid = "The index wait token belongs to another incarnation, atomic partition or placement.";
    private const string WaitTokenFuture = "The index wait token is beyond the current quorum-applied cut.";
    private const string WaitAppliedCorrupt = "The canonical applied position is invalid.";
    private const string WaitInvalidRequest = "A text field and acknowledged minimum token are required.";
    private const string WaitProviderUnavailable = "Native text index waiting is unavailable.";
    private const long WaitInitialPosition = 0;

    /// <summary>Publishes the eligible native text generation at one acknowledged authorized read cut.</summary>
    /// <param name="principalId">Fresh persisted principal identity.</param>
    /// <param name="request">Collection, field and acknowledged minimum token.</param>
    /// <param name="cancellationToken">Original admission and complete worker cancellation.</param>
    /// <returns>The observed applied cut after joined native generation settlement.</returns>
    public async Task<WaitForIndexResult> WaitForIndexAsync(string principalId, WaitForIndexRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var budget = new ReadExecutionBudget(database.OperationLimitsOptions, database.EvaluationClock, cancellationToken);
        QueryResultBudgetPolicy.Constrain(budget, execution);
        budget.Check();
        using var reservation = database.AdmitQuery(cancellationToken);
        return await Task.Run(() => WaitForIndexCore(principalId, request, budget), cancellationToken).ConfigureAwait(false);
    }

    private WaitForIndexResult WaitForIndexCore(string principalId, WaitForIndexRequest request, ReadExecutionBudget budget)
    {
        return database.WithQueryView(principalId, request.Partition, request.Collection, (view, principal, resource) =>
        {
            FilteredSearchRequestSizer.EnsureBounded(request, database.Limits.MaxQueryBytes, budget);
            if (string.IsNullOrEmpty(request.TextField) || request.MinimumToken is null)
            { throw Errors.Fail(ErrorCode.Validation, WaitInvalidRequest); }
            database.Authorization.RequireFieldUse(principal, resource, request.TextField);
            var path = JsonData.PathSegments(request.TextField);
            var authority = new BudgetedReadView(view, budget);
            DatabaseEngine.ValidateCommitToken(authority, request.Partition, request.MinimumToken, ErrorCode.TokenInvalidated, WaitTokenInvalid);
            var bytes = authority.ReadOwnedValue(KeySpace.AppliedBytes) ?? throw Errors.Fail(ErrorCode.Corruption, WaitAppliedCorrupt);
            var applied = NativeSerialization.Deserialize<long>(bytes);
            if (applied < WaitInitialPosition)
            { throw Errors.Fail(ErrorCode.Corruption, WaitAppliedCorrupt); }
            if (request.MinimumToken.Position > applied)
            { throw Errors.Fail(ErrorCode.TokenInvalidated, WaitTokenFuture); }
            var provider = textProjection ?? throw Errors.Fail(ErrorCode.UnsupportedCapability, WaitProviderUnavailable);
            var search = new SearchRequest(request.Partition, request.Collection, TextField: request.TextField);
            var lease = SelectedTextProjectionAdmission.Acquire(database, provider, view, principal, resource, search, budget)
                ?? throw Errors.Fail(ErrorCode.UnsupportedCapability, WaitProviderUnavailable);
            Exception? primaryFailure = null;
            WaitForIndexResult result;
            try
            {
                database.VisitVisibleDocuments(view, principal, request.Partition, request.Collection, budget,
                    document => PublishTextIndexRecord(lease, path, budget, document));
                lease.VerifyCandidates([], [], budget);
                result = new WaitForIndexResult(DatabaseEngine.Token(authority, request.Partition, applied), resource.SchemaVersion, principal.PolicyEpoch);
                budget.CheckResult(result);
            }
            catch (Exception error)
            { primaryFailure = error; throw; }
            finally
            { TextProjectionLifecycle.Dispose(lease, primaryFailure); }
            budget.CheckResult(result);
            return result;
        });
    }
}
