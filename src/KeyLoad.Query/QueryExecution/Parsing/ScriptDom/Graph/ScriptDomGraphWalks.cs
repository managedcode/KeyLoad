using Dom = Microsoft.SqlServer.TransactSql.ScriptDom;

namespace KeyLoad.Query.QueryExecution;

internal static class ScriptDomGraphWalks
{
    internal static (bool HasValue, GraphScope? Value) Scope(Dom.ScalarExpression expression, ScriptDomGraphContext context, int depth)
    {
        if (ScriptDomGraphShape.IsNull(expression, context, depth))
        {
            return (false, default(GraphScope?));
        }
        return (true, (GraphScope?)(new(Walk(ScriptDomGraphShape.Call(expression, ScopeFunction, context, depth, WalkArity), context, depth))));
    }
    internal static (bool HasValue, GraphRetriever? Value) Retriever(Dom.ScalarExpression expression, ScriptDomGraphContext context, int depth)
    {
        if (ScriptDomGraphShape.IsNull(expression, context, depth))
        {
            return (false, default(GraphRetriever?));
        }
        var arguments = ScriptDomGraphShape.Call(expression, RetrieverFunction, context, depth, RetrieverArity);
        return (true, (GraphRetriever?)(new(Walk(arguments, context, depth),
            ScriptDomGraphScalar.Weight(arguments[WeightIndex], context, depth + ChildDepth))));
    }
    internal static (bool HasValue, GraphExpansion? Value) Expansion(Dom.ScalarExpression expression, ScriptDomGraphContext context, int depth)
    {
        if (ScriptDomGraphShape.IsNull(expression, context, depth))
        {
            return (false, default(GraphExpansion?));
        }
        var arguments = ScriptDomGraphShape.Call(expression, ExpansionFunction, context, depth, ExpansionArity);
        return (true, (GraphExpansion?)(new(ScriptDomGraphScalar.String(arguments[GraphIndex], context, depth + ChildDepth),
            ScriptDomGraphScalar.Integer(arguments[ExpansionDepthIndex], context, depth + ChildDepth),
            ScriptDomGraphScalar.Integer(arguments[ExpansionVerticesIndex], context, depth + ChildDepth),
            ScriptDomGraphScalar.Integer(arguments[ExpansionEdgesIndex], context, depth + ChildDepth),
            (ScriptDomGraphScalar.Strings(arguments[ExpansionLabelsIndex], LabelsFunction, context, depth + ChildDepth)).Value)));
    }
    private static GraphWalkSpec Walk(IList<Dom.ScalarExpression> arguments, ScriptDomGraphContext context, int depth)
        => new(ScriptDomGraphScalar.String(arguments[GraphIndex], context, depth + ChildDepth),
            ScriptDomGraphSeeds.Read(arguments[SeedsIndex], context, depth + ChildDepth),
            ScriptDomGraphScalar.Integer(arguments[DepthIndex], context, depth + ChildDepth),
            ScriptDomGraphScalar.Integer(arguments[VerticesIndex], context, depth + ChildDepth),
            ScriptDomGraphScalar.Integer(arguments[EdgesIndex], context, depth + ChildDepth),
            (ScriptDomGraphScalar.Strings(arguments[LabelsIndex], LabelsFunction, context, depth + ChildDepth)).Value);
    private const string ScopeFunction = "GRAPH_SCOPE";
    private const string RetrieverFunction = "GRAPH_RETRIEVER";
    private const string ExpansionFunction = "GRAPH_EXPANSION";
    private const string LabelsFunction = "GRAPH_LABELS";
    private const int WalkArity = 6;
    private const int RetrieverArity = 7;
    private const int ExpansionArity = 5;
    private const int GraphIndex = 0;
    private const int SeedsIndex = 1;
    private const int DepthIndex = 2;
    private const int VerticesIndex = 3;
    private const int EdgesIndex = 4;
    private const int LabelsIndex = 5;
    private const int WeightIndex = 6;
    private const int ExpansionDepthIndex = 1;
    private const int ExpansionVerticesIndex = 2;
    private const int ExpansionEdgesIndex = 3;
    private const int ExpansionLabelsIndex = 4;
    private const int ChildDepth = 1;
}
