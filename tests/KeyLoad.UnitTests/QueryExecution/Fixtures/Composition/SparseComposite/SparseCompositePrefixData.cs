using System.Collections.Immutable;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SparseCompositePrefixData
{
    internal const string Composite = "status_rank";
    internal const string StatusIndex = "status_only";
    internal const string Only = "sparse_only";
    internal const string WithStatusIndex = "sparse_with_status_index";
    internal const string Reader = "sparse-prefix-reader";
    internal const string ReadGrant = "status.read";
    internal const string UseGrant = "status.use";
    internal const string Scan = "bounded-full-scan";
    internal static readonly (string Id, string Json)[] Rows =
    [
        ("a", """{"status":"open"}"""),
        ("b", """{"status":"open","rank":null}"""),
        ("c", """{"status":"open","rank":7}"""),
        ("d", """{"status":"open","rank":8}"""),
        ("e", """{"status":"closed","rank":7}"""),
        ("f", """{"status":null,"rank":7}"""),
        ("g", """{"rank":7}""")
    ];
    internal static ImmutableArray<IndexDefinition> Indexes(bool includeNull, bool includeMissing, bool includeStatusIndex)
    {
        var index = new IndexDefinition(Composite, ["/status", "/rank"], IncludeNull: includeNull, IncludeMissing: includeMissing);
        return includeStatusIndex ? [index, new(StatusIndex, ["/status"])] : [index];
    }
    internal static string Path(bool includeNull, bool includeMissing, bool includeStatusIndex, int shape)
        => shape != 0 || includeNull && includeMissing ? "index:" + Composite
            : includeStatusIndex ? "index:" + StatusIndex : Scan;
    internal static QueryRow[] Expected(int shape)
        => Rows.Take(4).Where(row => shape == 0 || shape == 1 && row.Id == "c")
            .Select(row => new QueryRow(row.Id, 1L, row.Json, false, [])).ToArray();
}
