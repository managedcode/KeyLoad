using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using Microsoft.Extensions.Options;

namespace KeyLoad.Query.Features.Search;

internal static class AnnWaitExecution
{
    private const long InitialPosition = 0;
    private const string Invalid = "The ANN wait requires a vector scope, generation and acknowledged minimum token.";
    private const string WrongToken = "The ANN wait token belongs to another incarnation, atomic partition or placement.";
    private const string Future = "The ANN wait token is beyond the current quorum-applied cut.";
    private const string Corrupt = "The native ANN indexed or canonical applied prefix is invalid.";
    private const string Missing = "The native ANN generation does not cover the required prefix; explicitly restore the generation.";

    internal static async Task<WaitForAnnIndexResult> ExecuteAsync(DatabaseEngine database, IAnnProjection? projection,
        string principalId, WaitForAnnIndexRequest request, IOptions<QueryExecutionOptions> configured,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var execution = configured.Value;
        if (!execution.EnableApproximateSearch)
        { throw Errors.Fail(ErrorCode.UnsupportedCapability, AnnSearchProtocol.Disabled); }
        var budget = new ReadExecutionBudget(database.OperationLimitsOptions, database.EvaluationClock, cancellationToken);
        QueryResultBudgetPolicy.Constrain(budget, execution);
        budget.Check();
        using var reservation = database.AdmitQuery(cancellationToken);
        return await Task.Run(() => Read(database, projection, principalId, request, budget), cancellationToken).ConfigureAwait(false);
    }

    private static WaitForAnnIndexResult Read(DatabaseEngine database, IAnnProjection? projection,
        string principalId, WaitForAnnIndexRequest request, ReadExecutionBudget budget)
        => database.WithQueryView(principalId, request.Partition, request.Collection, (view, principal, resource) =>
        {
            FilteredSearchRequestSizer.EnsureBounded(request, database.Limits.MaxQueryBytes, budget);
            database.Authorization.Require(principal, request.Partition, request.Collection, Capability.VectorSearch);
            if (string.IsNullOrEmpty(request.VectorField) || request.Space is null || request.Consumer is null
                || request.Consumer.Partition != request.Partition || request.IndexGeneration <= InitialPosition
                || request.MinimumToken is null)
            { throw Errors.Fail(ErrorCode.Validation, Invalid); }
            database.Authorization.RequireFieldUse(principal, resource, request.VectorField);
            JsonData.Identifier(request.Consumer.Name);
            var authority = new BudgetedReadView(view, budget);
            DatabaseEngine.ValidateCommitToken(authority, request.Partition, request.MinimumToken, ErrorCode.TokenInvalidated, WrongToken);
            var raw = authority.ReadOwnedValue(KeySpace.AppliedBytes) ?? throw Errors.Fail(ErrorCode.Corruption, Corrupt);
            var applied = NativeSerialization.Deserialize<long>(raw);
            if (applied < InitialPosition)
            { throw Errors.Fail(ErrorCode.Corruption, Corrupt); }
            if (request.MinimumToken.Position > applied)
            { throw Errors.Fail(ErrorCode.TokenInvalidated, Future); }
            var owner = projection ?? throw Errors.Fail(ErrorCode.HistoryUnavailable, Missing);
            var lease = owner.Acquire(view, new(request.Partition, request.Collection, request.VectorField,
                request.Space, request.Consumer, request.IndexGeneration), budget);
            Exception? primary = null;
            WaitForAnnIndexResult result;
            try
            {
                var indexed = lease.IndexedAppliedPosition;
                if (indexed < InitialPosition || indexed > applied || lease.IndexGeneration != request.IndexGeneration)
                { throw Errors.Fail(ErrorCode.Corruption, Corrupt); }
                if (indexed < request.MinimumToken.Position)
                { throw Errors.Fail(ErrorCode.HistoryUnavailable, Missing); }
                result = new(DatabaseEngine.Token(authority, request.Partition, indexed), lease.IndexGeneration,
                    resource.SchemaVersion, principal.PolicyEpoch);
                budget.CheckResult(result);
            }
            catch (Exception error) { primary = error; throw; }
            finally { Dispose(lease, primary); }
            budget.CheckResult(result);
            return result;
        });

    private static void Dispose(IAnnProjectionLease lease, Exception? primary)
    {
        try
        { lease.Dispose(); }
        catch (Exception cleanup)
        {
            if (primary is not null)
            { throw new AggregateException(primary, cleanup); }
            throw;
        }
    }
}
