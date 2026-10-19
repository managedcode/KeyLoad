using Dom = Microsoft.SqlServer.TransactSql.ScriptDom;

namespace KeyLoad.Server;

internal static class ScriptDomGraphToQueueInsert
{
    internal static (Guid Id, Mutation Mutation) Read(Dom.QuerySpecification query,
        ScriptDomCompositionParameters parameters, int version)
    {
        if (version != SqlOperationProtocol.DefaultQueryDialectVersion)
        {
            throw ScriptDomOperationErrors.Unsupported();
        }
        var values = ScriptDomInsertShape.Values(query, ValueCount);
        var id = parameters.Read<Guid>(values[CommandIndex]);
        var queue = parameters.Read<string>(values[QueueIndex]);
        var prefix = parameters.Read<string>(values[PrefixIndex]);
        var source = ScriptDomInsertShape.Source(query.FromClause.TableReferences[FirstIndex], ScriptDomInsertShape.GraphTraverse, SourceCount);
        return (id, new GraphToQueueMutation(queue, parameters.Read<string>(source.Parameters[GraphIndex]),
            parameters.Read<EntityRef>(source.Parameters[EntityIndex]), prefix, parameters.Read<int>(source.Parameters[DepthIndex]),
            parameters.Read<int>(source.Parameters[VerticesIndex]), parameters.Read<int>(source.Parameters[EdgesIndex])));
    }
    private const int ValueCount = 3;
    private const int SourceCount = 5;
    private const int CommandIndex = 0;
    private const int QueueIndex = 1;
    private const int PrefixIndex = 2;
    private const int FirstIndex = 0;
    private const int GraphIndex = 0;
    private const int EntityIndex = 1;
    private const int DepthIndex = 2;
    private const int VerticesIndex = 3;
    private const int EdgesIndex = 4;
}
