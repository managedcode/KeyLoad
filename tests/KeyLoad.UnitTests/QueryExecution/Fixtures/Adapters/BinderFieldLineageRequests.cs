using System.Collections.Immutable;
using KeyLoad.Query;
using static KeyLoad.UnitTests.QueryExecution.BinderFieldLineageProtocol;

namespace KeyLoad.UnitTests.QueryExecution;

internal static class BinderFieldLineageRequests
{
    internal const string OriginalJson = """{"people":[{"name":"A","secret":"CANARY"}],"odd.name":"CANARY","status":"open"}""";
    internal const string Projection = "d.[odd.name] AS public_value, d.status AS [odd.name], d.people.[0].secret AS nested_value";
    internal static readonly ImmutableArray<Selection> Selections =
        [new(DottedPolicy, PublicAlias), new(StatusPath, ProtectedLookingAlias), new(ArrayElementPath, NestedAlias)];
    private const int DefaultLimit = 100;
    private const string SelectPrefix = "SELECT ";
    private const string SourceClause = " FROM orders d";
    private const string ProtectedPredicate = " WHERE d.people.[0].secret = 'CANARY'";
    private const string ProtectedOrder = " ORDER BY d.[odd.name]";
    private const string TableAlias = "d";
    private const string EqualOperator = "=";

    internal static string Sql(int shape) => SelectPrefix + Projection + SourceClause + (shape switch
    {
        PredicateShape => ProtectedPredicate,
        SortShape => ProtectedOrder,
        _ => string.Empty
    });

    internal static AstQueryRequest Ast(PartitionRef partition, int shape) => new(partition,
        new(Collection, TableAlias, Selections,
            shape == PredicateShape ? new Comparison(new FieldOperand(ArrayElementPath), EqualOperator,
                new ValueOperand(System.Text.Json.JsonSerializer.SerializeToElement(Canary))) : null,
            shape == SortShape ? [new(DottedPolicy, false)] : [], DefaultLimit), AllowFullScan: true);

    private const string VisibleJson = """{"public_value":"CANARY","odd.name":"open","nested_value":"CANARY"}""";
    private const string MaskedJson = """{"public_value":null,"odd.name":"open","nested_value":null}""";

    internal static QueryPage Expected(long cut, bool read)
        => new([new(DocumentId, OriginalRevision, read
            ? VisibleJson
            : MaskedJson,
            !read, read ? [] : [ArrayPolicy, DottedPolicy])], null, cut, ScanPath);
}
