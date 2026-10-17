using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class QueueViewLeaseIsolationRequests
{
    internal static QueryRequest Sql(QueueViewLeaseIsolationState state, bool hidden)
        => state.Scenario.QueueSql() with
        {
            Sql = hidden ? QueueViewLeaseIsolationProtocol.HiddenSql : state.Scenario.QueueSql().Sql
        };

    internal static AstQueryRequest Ast(QueueViewLeaseIsolationState state, bool hidden)
    {
        var request = state.Scenario.QueueAst();
        if (!hidden)
        {
            return request;
        }
        return request with
        {
            Query = request.Query with
            {
                Projection = QueueViewLeaseIsolationProtocol.HiddenSelections
            }
        };
    }
}
