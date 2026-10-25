using KeyLoad.Query;
using static KeyLoad.FunctionalTests.QueryExecution.QueueViewLeaseIsolationProtocol;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class QueueViewLeaseIsolationRefusals
{
    internal static async Task RunAsync(QueueViewLeaseIsolationState state)
    {
        var queries = new QueryEngine(state.Owner.Database, UnitExecutionOptions.QueryExecution());
        await DeniedAsync(state, () => queries.Execute(Reader,
            QueueViewLeaseIsolationRequests.Sql(state) with
            {
                Sql = PrivateSql
            }, cancellationToken: state.Token), ErrorCode.PermissionDenied);
        var ast = QueueViewLeaseIsolationRequests.Ast(state);
        await DeniedAsync(state, () => queries.ExecuteAst(Reader, ast with
        {
            Query = ast.Query with
            {
                Filter = new Comparison(new FieldOperand(PrivateField), Equal, ValueOperand.Create(PrivateValue))
            }
        }, cancellationToken: state.Token), ErrorCode.PermissionDenied);
        await DeniedAsync(state, () => queries.Execute(Reader,
            QueueViewLeaseIsolationRequests.Sql(state) with
            {
                Cursor = UnsupportedCursor
            }, cancellationToken: state.Token), ErrorCode.UnsupportedCapability);
        await DeniedAsync(state, () => queries.ExecuteAst(Reader,
            ast with
            {
                Cursor = UnsupportedCursor
            }, cancellationToken: state.Token), ErrorCode.UnsupportedCapability);
        await DeniedAsync(state, () => queries.Execute(Root,
            QueueViewLeaseIsolationRequests.Sql(state) with
            {
                Sql = StaleSql
            }, cancellationToken: state.Token), ErrorCode.TokenInvalidated);
        await QueueViewLeaseIsolationReads.ObserveAsync(state, async () =>
        {
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => SqlOperationTestData.Compile(new(state.Partition, UpdateSql), cancellationToken: state.Token));
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.UnsupportedCapability);
        });
        await CancelledAsync(state, queries, ast);
    }

    internal static Task DeniedAsync(QueueViewLeaseIsolationState state, Func<QueryPage> operation, ErrorCode expected)
        => QueueViewLeaseIsolationReads.ObserveAsync(state, async () =>
        {
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => operation());
            await Assert.That(failure.Code).IsEqualTo(expected);
            await Assert.That(failure.Message.Contains(PrivateValue, StringComparison.Ordinal)
                || failure.Message.Contains(state.Delivery.Token, StringComparison.Ordinal)).IsFalse();
        });

    private static async Task CancelledAsync(QueueViewLeaseIsolationState state, QueryEngine queries, AstQueryRequest ast)
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await QueueViewLeaseIsolationReads.ObserveAsync(state, () =>
        {
            Assert.Throws<OperationCanceledException>(() => queries.Execute(Reader,
                QueueViewLeaseIsolationRequests.Sql(state), cancellationToken: cancelled.Token));
            Assert.Throws<OperationCanceledException>(() => queries.ExecuteAst(Reader, ast,
                cancellationToken: cancelled.Token));
            return Task.CompletedTask;
        });
    }
}
