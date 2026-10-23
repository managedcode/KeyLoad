using System.Text.Json;
using KeyLoad.Query;
using static KeyLoad.UnitTests.Features.QueryExecution.QueueViewLeaseIsolationProtocol;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class QueueViewLeaseIsolationReads
{
    internal static async Task RunAsync(QueueViewLeaseIsolationState state)
    {
        var queries = new QueryEngine(state.Owner.Database, UnitExecutionOptions.QueryExecution());
        foreach (var hidden in new[] { false, true })
        {
            await ObserveAsync(state, async () => await QueueViewLeaseIsolationRows.SameAsync(
                queries.Execute(Reader, QueueViewLeaseIsolationRequests.Sql(state, hidden), cancellationToken: state.Token),
                QueueViewLeaseIsolationRows.Page(state, hidden)));
            await ObserveAsync(state, async () => await QueueViewLeaseIsolationRows.SameAsync(
                queries.ExecuteAst(Reader, QueueViewLeaseIsolationRequests.Ast(state, hidden), cancellationToken: state.Token),
                QueueViewLeaseIsolationRows.Page(state, hidden)));
            var decoded = SqlOperationTestData.Compile(new(state.Partition,
                hidden ? HiddenSql : QueueSql, AllowFullScan: true), cancellationToken: state.Token);
            // The SQL gateway lowers SELECT once and ships the typed plan.
            var request = OrleansSerialization.Deserialize<AstQueryRequest>(decoded.Payload.Span);
            await ObserveAsync(state, async () => await QueueViewLeaseIsolationRows.SameAsync(
                queries.ExecuteAst(Reader, request, cancellationToken: state.Token), QueueViewLeaseIsolationRows.Page(state, hidden)));
        }
        await ExplainAsync(state, queries);
    }

    private static Task ExplainAsync(QueueViewLeaseIsolationState state, QueryEngine queries)
        => ObserveAsync(state, async () =>
        {
            var request = QueueViewLeaseIsolationRequests.Ast(state);
            var actual = queries.ExecuteAst(Reader, request with
            {
                Query = request.Query with
                {
                    Explain = true
                }
            }, cancellationToken: state.Token);
            var json = JsonSerializer.Serialize(new
            {
                AccessPath = QueuePath,
                ModelViews = true,
                ScanBudget = state.Owner.Database.Limits.MaxScanRecords
            }, JsonDefaults.Options);
            await QueueViewLeaseIsolationRows.SameAsync(actual, new([new(ExplainId, Empty, json)],
                null, state.Owner.Store.Position, QueuePath));
        });

    internal static async Task ObserveAsync(QueueViewLeaseIsolationState state, Func<Task> operation)
    {
        state.Token.ThrowIfCancellationRequested();
        var before = QueueViewLeaseIsolationImages.Capture(state.Owner);
        var failures = new List<Exception>();
        await KeyLoad.Server.ServerFailureObserver.ObserveAsync(operation, failures);
        await KeyLoad.Server.ServerFailureObserver.ObserveAsync(
            () => QueueViewLeaseIsolationImages.SameAsync(state.Owner, before), failures);
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
}
