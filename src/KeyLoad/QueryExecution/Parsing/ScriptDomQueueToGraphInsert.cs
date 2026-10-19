using Dom = Microsoft.SqlServer.TransactSql.ScriptDom;

namespace KeyLoad.Server;

internal static class ScriptDomQueueToGraphInsert
{
    internal static (Guid Id, Mutation Mutation) Read(Dom.QuerySpecification query,
        ScriptDomCompositionParameters parameters, int version)
    {
        var values = ScriptDomInsertShape.Values(query, ValueCount);
        var id = parameters.Read<Guid>(values[CommandIndex]);
        var graph = parameters.Read<string>(values[GraphIndex]);
        var prefix = parameters.Read<string>(values[PrefixIndex]);
        var maximum = parameters.Read<int>(values[MaximumIndex]);
        var table = query.FromClause.TableReferences[FirstIndex];
        string queue;
        string? from = null;
        string? to = null;
        if (version == SqlCompositionInsert.JoinedVersion)
        {
            (queue, from, to) = ScriptDomCompositionEndpointJoin.Read(table, parameters);
        }
        else
        {
            queue = parameters.Read<string>(ScriptDomInsertShape.Source(table, ScriptDomInsertShape.QueueMessages, One).Parameters[FirstIndex]);
        }
        return (id, new QueueToGraph(graph, queue, prefix, maximum, from, to));
    }
    private const int ValueCount = 4;
    private const int CommandIndex = 0;
    private const int GraphIndex = 1;
    private const int PrefixIndex = 2;
    private const int MaximumIndex = 3;
    private const int FirstIndex = 0;
    private const int One = 1;
}
