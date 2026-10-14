using System.Collections.Immutable;
using KeyLoad.Features.QueryExecution;
using Dom = Microsoft.SqlServer.TransactSql.ScriptDom;

namespace KeyLoad.Query.Features.QueryExecution;

internal static class ScriptDomGraphSeeds
{
    internal static ImmutableArray<EntityRef> Read(Dom.ScalarExpression expression, ScriptDomGraphContext context, int depth)
    {
        var arguments = ScriptDomGraphShape.Call(expression, SeedsFunction, context, depth);
        if (arguments.Count == EmptyCount)
        {
            throw ScriptDomErrors.Invalid();
        }
        var seeds = ImmutableArray.CreateBuilder<EntityRef>(arguments.Count);
        foreach (var argument in arguments)
        {
            var entity = ScriptDomGraphShape.Call(argument, EntityFunction, context, depth + ChildDepth, EntityArity);
            seeds.Add(new(context.Query.Partition,
                ScriptDomGraphScalar.String(entity[CollectionIndex], context, depth + EntityDepth),
                ScriptDomGraphScalar.String(entity[IdentityIndex], context, depth + EntityDepth)));
        }
        return seeds.ToImmutable();
    }
    private const string SeedsFunction = "GRAPH_SEEDS";
    private const string EntityFunction = "ENTITY";
    private const int EmptyCount = 0;
    private const int EntityArity = 2;
    private const int CollectionIndex = 0;
    private const int IdentityIndex = 1;
    private const int ChildDepth = 1;
    private const int EntityDepth = 2;
}
