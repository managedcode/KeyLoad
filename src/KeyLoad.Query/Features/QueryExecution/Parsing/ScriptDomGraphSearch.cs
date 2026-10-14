using KeyLoad.Features.QueryExecution;
using Dom = Microsoft.SqlServer.TransactSql.ScriptDom;

namespace KeyLoad.Query.Features.QueryExecution;

internal static class ScriptDomGraphSearch
{
    internal const string Function = "GRAPH_SEARCH";
    internal static GraphSearchRequest Read(IList<Dom.ScalarExpression> arguments, ScriptDomGraphContext context)
    {
        if (arguments.Count != ArgumentCount)
        { throw ScriptDomErrors.Invalid(); }
        var collection = ScriptDomGraphScalar.String(arguments[CollectionIndex], context, ValueDepth);
        var text = ScriptDomGraphChannels.Text(arguments[TextIndex], context, ValueDepth);
        var vector = ScriptDomGraphChannels.Vector(arguments[VectorIndex], context, ValueDepth);
        var scope = ScriptDomGraphWalks.Scope(arguments[ScopeIndex], context, ValueDepth);
        var retriever = ScriptDomGraphWalks.Retriever(arguments[RetrieverIndex], context, ValueDepth);
        var expansion = ScriptDomGraphWalks.Expansion(arguments[ExpansionIndex], context, ValueDepth);
        var allowed = ScriptDomGraphScalar.Strings(arguments[AllowedIndex], IdsFunction, context, ValueDepth);
        var limit = ScriptDomGraphScalar.Integer(arguments[LimitIndex], context, ValueDepth);
        var fusion = ScriptDomGraphScalar.Integer(arguments[FusionIndex], context, ValueDepth);
        if (scope is null && retriever is null && expansion is null)
        { throw ScriptDomErrors.Invalid(); }
        var search = new SearchRequest(context.Query.Partition, collection, text.Field, text.Text,
            vector.Field, vector.Values, vector.Space, limit, text.Weight, vector.Weight, fusion, allowed);
        return new(SqlGraphSearchSyntax.Version, search, scope, retriever, expansion);
    }
    private const string IdsFunction = "GRAPH_IDS";
    private const int ArgumentCount = 9;
    private const int CollectionIndex = 0;
    private const int TextIndex = 1;
    private const int VectorIndex = 2;
    private const int ScopeIndex = 3;
    private const int RetrieverIndex = 4;
    private const int ExpansionIndex = 5;
    private const int AllowedIndex = 6;
    private const int LimitIndex = 7;
    private const int FusionIndex = 8;
    private const int ValueDepth = 3;
}
