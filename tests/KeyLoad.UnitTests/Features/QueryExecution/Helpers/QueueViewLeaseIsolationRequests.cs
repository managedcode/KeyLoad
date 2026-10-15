using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class QueueViewLeaseIsolationRequests
{
    internal static QueryRequest Sql(QueueViewLeaseIsolationState state, bool hidden = false)
        => new(state.Partition, hidden ? QueueViewLeaseIsolationProtocol.HiddenSql : QueueViewLeaseIsolationProtocol.QueueSql,
            AllowFullScan: true);

    internal static AstQueryRequest Ast(QueueViewLeaseIsolationState state, bool hidden = false)
        => new(state.Partition, new(QueueViewLeaseIsolationProtocol.Queue, null,
            hidden ? QueueViewLeaseIsolationProtocol.HiddenSelections : [new(QueueViewLeaseIsolationProtocol.Star, QueueViewLeaseIsolationProtocol.Star)],
            null, [new(QueueViewLeaseIsolationProtocol.Identifier, false)], QueueViewLeaseIsolationProtocol.RowLimit,
            ModelSource: new(ModelQuerySourceKind.QueueMessages, QueueViewLeaseIsolationProtocol.Queue)), AllowFullScan: true);
}
