using KeyLoad.Query;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class ModelHeadAuthorityRequests
{
    internal static QueryRequest Sql(PartitionRef partition, bool queue, long generation = 1L)
        => new(partition, queue ? "SELECT TOP (10) * FROM QUEUE_MESSAGES('model-head-queue') ORDER BY id"
            : "SELECT TOP (10) * FROM EVENTS('model-head-events','source'," + generation.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ") ORDER BY revision", AllowFullScan: true);
    internal static AstQueryRequest Ast(PartitionRef partition, bool queue, long generation = 1L)
        => new(partition, new(queue ? ModelHeadAuthorityFixture.Queue : ModelHeadAuthorityFixture.Events,
            null, [new("*", "*")], null, [new(queue ? "/id" : "/revision", false)], 10,
            ModelSource: new(queue ? ModelQuerySourceKind.QueueMessages : ModelQuerySourceKind.Events,
                queue ? ModelHeadAuthorityFixture.Queue : ModelHeadAuthorityFixture.Stream, generation)), AllowFullScan: true);
    internal static StreamHead Fault(int fault) => fault switch
    {
        0 => new(2L, 1L, 0L),
        1 => new(2L, 1L, -1L),
        2 => new(2L, 1L, long.MinValue),
        3 => new(-1L, 1L, 2L),
        4 => new(2L, 0L, 2L),
        _ => new(2L, 4L, 2L)
    };
}
