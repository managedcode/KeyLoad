using System.Collections.Immutable;
using Dom = Microsoft.SqlServer.TransactSql.ScriptDom;

namespace KeyLoad.Query.Features.QueryExecution;

internal static class ScriptDomGraphPath
{
    internal static GraphShortestPathRequest Read(IList<Dom.ScalarExpression> arguments,
        ScriptDomGraphContext context, int maximumLabels)
    {
        if (arguments.Count < SqlGraphPathSyntax.MandatoryArgumentCount
            || arguments.Count - SqlGraphPathSyntax.MandatoryArgumentCount > maximumLabels)
        {
            throw SqlGraphPathSyntax.Invalid();
        }
        var graph = ScriptDomGraphScalar.String(arguments[0], context, ValueDepth);
        var fromCollection = ScriptDomGraphScalar.String(arguments[1], context, ValueDepth);
        var fromId = ScriptDomGraphScalar.String(arguments[FromIdIndex], context, ValueDepth);
        var toCollection = ScriptDomGraphScalar.String(arguments[ToCollectionIndex], context, ValueDepth);
        var toId = ScriptDomGraphScalar.String(arguments[ToIdIndex], context, ValueDepth);
        var depth = ScriptDomGraphScalar.Integer(arguments[DepthIndex], context, ValueDepth);
        var vertices = ScriptDomGraphScalar.Integer(arguments[VerticesIndex], context, ValueDepth);
        var edges = ScriptDomGraphScalar.Integer(arguments[EdgesIndex], context, ValueDepth);
        ImmutableArray<string>? labels = null;
        if (arguments.Count > SqlGraphPathSyntax.MandatoryArgumentCount)
        {
            var builder = ImmutableArray.CreateBuilder<string>();
            for (var index = SqlGraphPathSyntax.MandatoryArgumentCount; index < arguments.Count; index++)
            {
                builder.Add(ScriptDomGraphScalar.String(arguments[index], context, ValueDepth));
            }
            labels = builder.ToImmutable();
        }
        var partition = context.Query.Partition;
        return new(SqlGraphPathSyntax.Version, partition, graph, new(partition, fromCollection, fromId),
            new(partition, toCollection, toId), depth, vertices, edges, labels);
    }
    private const int FromIdIndex = 2;
    private const int ToCollectionIndex = 3;
    private const int ToIdIndex = 4;
    private const int DepthIndex = 5;
    private const int VerticesIndex = 6;
    private const int EdgesIndex = 7;
    private const int ValueDepth = 3;
}
