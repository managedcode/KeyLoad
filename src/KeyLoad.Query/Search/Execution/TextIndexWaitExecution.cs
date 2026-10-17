using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Query.Features.Search;

internal static class TextIndexWaitExecution
{
    private const string WaitTokenInvalid = "The index wait token belongs to another incarnation, atomic partition or placement.";
    private const string WaitTokenFuture = "The index wait token is beyond the current quorum-applied cut.";
    private const string WaitAppliedCorrupt = "The canonical applied position is invalid.";
    private const string WaitInvalidRequest = "A text field and acknowledged minimum token are required.";
    private const string WaitProviderUnavailable = "Native text index waiting is unavailable.";
    private const long WaitInitialPosition = 0;

    internal static WaitForIndexResult Execute(DatabaseEngine database, ITextProjection? textProjection,
        QueryExecutionOptions execution, IQueryReadCutObserver? readCutObserver, string principalId,
        WaitForIndexRequest request, ReadExecutionBudget budget)
    {
        WaitForIndexReadCutObservation? observation = null;
        var completed = database.WithQueryView(principalId, request.Partition, request.Collection, Capability.None, (view, principal, resource) =>
        {
            FilteredSearchRequestSizer.EnsureBounded(request, database.Limits.MaxQueryBytes, budget);
            if (string.IsNullOrEmpty(request.TextField) || request.MinimumToken is null)
            {
                throw Errors.Fail(ErrorCode.Validation, WaitInvalidRequest);
            }
            database.Authorization.RequireFieldUse(principal, resource, request.TextField);
            var path = JsonData.PathSegments(request.TextField);
            var authority = new BudgetedReadView(view, budget);
            DatabaseEngine.ValidateCommitToken(authority, request.Partition, request.MinimumToken, ErrorCode.TokenInvalidated, WaitTokenInvalid);
            var bytes = authority.ReadOwnedValue(KeySpace.AppliedBytes) ?? throw Errors.Fail(ErrorCode.Corruption, WaitAppliedCorrupt);
            var applied = NativeSerialization.Deserialize<long>(bytes);
            if (applied < WaitInitialPosition)
            {
                throw Errors.Fail(ErrorCode.Corruption, WaitAppliedCorrupt);
            }
            if (request.MinimumToken.Position > applied)
            {
                throw Errors.Fail(ErrorCode.TokenInvalidated, WaitTokenFuture);
            }
            var provider = textProjection ?? throw Errors.Fail(ErrorCode.UnsupportedCapability, WaitProviderUnavailable);
            var search = new SearchRequest(request.Partition, request.Collection, TextField: request.TextField);
            var lease = SelectedTextProjectionAdmission.Acquire(database, provider, view, principal, resource, search, budget)
                ?? throw Errors.Fail(ErrorCode.UnsupportedCapability, WaitProviderUnavailable);
            Exception? primaryFailure = null;
            WaitForIndexResult result;
            try
            {
                database.VisitVisibleDocuments(view, principal, request.Partition, request.Collection, budget,
                    document => NativeTextIndexPublication.Publish(lease, path, budget, document, execution));
                lease.VerifyCandidates([], [], budget);
                result = new WaitForIndexResult(DatabaseEngine.Token(authority, request.Partition, applied), resource.SchemaVersion, principal.PolicyEpoch);
                budget.CheckResult(result);
            }
            catch (Exception error)
            {
                primaryFailure = error;
                throw;
            }
            finally
            {
                TextProjectionLifecycle.Dispose(lease, primaryFailure);
            }
            budget.CheckResult(result);
            observation = readCutObserver is null ? null
                : WaitForIndexReadCutObservationDispatch.Capture(database, principal, resource, request, result, budget);
            return result;
        }, budget);
        WaitForIndexReadCutObservationDispatch.Observe(readCutObserver, observation, budget);
        return completed;
    }
}
