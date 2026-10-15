using System.Collections.Immutable;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class QueryRuleOptimizationRf3Data
{
    internal const int UpdatedRowIndex = 1;
    internal const int RevisionOnlyCollection = 0;
    internal const int LeadingMetadataCollection = 1;
    internal const int TrailingMetadataCollection = 2;
    internal const int RevisionOneShape = 0;
    internal const int OpenRevisionOneShape = 1;
    internal const int OpenRevisionTwoShape = 2;
    internal const int RawShadowShape = 3;
    internal const int NestedShape = 4;
    internal const string StatusPath = "/status";
    internal const string NestedPath = "/nested/@revision";
    internal const string RankPath = "/rank";
    internal const string EqualsOperator = "=";
    internal const string AndOperator = "AND";
    internal const string OrOperator = "OR";
    internal const string NestedIndexPath = "index:nested";
    internal const string StatusIndexPath = "index:status";
    internal const string MetadataIndexPath = "index:metadata";
    internal const int SqlSdkRoute = 0;
    internal const int SqlMcpRoute = 1;
    internal const int UnifiedSdkRoute = 2;
    internal const int UnifiedMcpRoute = 3;
    internal const int AstSdkRoute = 4;
    internal const int AstMcpRoute = 5;

    internal const string Reader = "rule-optimization-reader";
    internal const string ReadGrant = "optimizer.read";
    internal const string UseGrant = "optimizer.use";
    internal const string Scan = "bounded-full-scan";
    internal const string RevisionPath = "/@revision";
    internal const int PageSize = 2;
    internal static readonly string[] Collections = ["rule_revision", "rule_leading", "rule_trailing"];
    internal static readonly (string Id, long Revision, string Json)[] Rows =
    [
        ("a", 1L, """{"status":"open","rank":30,"@revision":99,"nested":{"@revision":7}}"""),
        ("b", 2L, """{"status":"open","rank":10,"@revision":1,"nested":{"@revision":9}}"""),
        ("c", 1L, """{"status":"closed","rank":0,"@revision":2,"nested":{"@revision":9}}"""),
        ("d", 1L, """{"status":"open","rank":20,"nested":{"@revision":7}}"""),
        ("e", 1L, """{"status":"open","rank":40,"@revision":null,"nested":{"@revision":9}}""")
    ];
    internal static ResourceDefinition Resource(PartitionRef partition, int collection, bool includeNull, bool includeMissing)
    {
        ImmutableArray<string> fields = collection switch
        {
            LeadingMetadataCollection => [RevisionPath, StatusPath],
            TrailingMetadataCollection => [StatusPath, RevisionPath],
            _ => [RevisionPath]
        };
        var indexes = ImmutableArray.CreateBuilder<IndexDefinition>();
        indexes.Add(new("metadata", fields, IncludeNull: includeNull, IncludeMissing: includeMissing));
        indexes.Add(new("nested", [NestedPath]));
        if (collection == LeadingMetadataCollection)
        {
            indexes.Add(new("status", [StatusPath]));
        }
        return new(Collections[collection], ResourceKind.Collection, partition.TransactionDomainId)
        {
            Indexes = indexes.ToImmutable(),
            FieldPolicies = [new(RevisionPath, "restricted", ReadGrant, UseGrant), new(StatusPath, "restricted", ReadGrant, UseGrant),
                new(NestedPath, "restricted", ReadGrant, UseGrant)]
        };
    }
    internal static CommandRequest Seed(PartitionRef partition) => new(Guid.NewGuid(), partition,
        [.. Collections.SelectMany(collection => Rows.Select(row => (Mutation)new PutDocument(collection, row.Id,
            row.Id == "b" ? """{"status":"open","rank":10,"@revision":2,"nested":{"@revision":9}}""" : row.Json)))]);
    internal static CommandRequest Update(PartitionRef partition) => new(Guid.NewGuid(), partition,
        [.. Collections.Select(collection => (Mutation)new PutDocument(collection, "b", Rows[UpdatedRowIndex].Json, 1L))]);
    internal static string Path(int collection, int shape, bool reference, bool includeNull, bool includeMissing)
    {
        if (reference)
        {
            return Scan;
        }
        if (shape == NestedShape)
        {
            return NestedIndexPath;
        }
        if (shape is RevisionOneShape or RawShadowShape)
        {
            return Scan;
        }
        if (collection == LeadingMetadataCollection)
        {
            return StatusIndexPath;
        }
        return collection == TrailingMetadataCollection && includeNull && includeMissing ? MetadataIndexPath : Scan;
    }
    internal static QueryRow[] Expected(int shape)
    {
        string[] ids = shape switch
        {
            RevisionOneShape => ["c", "d", "a", "e"],
            OpenRevisionOneShape => ["d", "a", "e"],
            OpenRevisionTwoShape => ["b"],
            RawShadowShape => [],
            _ => ["d", "a"]
        };
        return ids.Select(id => id switch
        {
            "a" => new QueryRow("a", 1L, """{"status":"open","rank":30}""", false, []),
            "b" => new QueryRow("b", 2L, """{"status":"open","rank":10}""", false, []),
            "c" => new QueryRow("c", 1L, """{"status":"closed","rank":0}""", false, []),
            "d" => new QueryRow("d", 1L, """{"status":"open","rank":20}""", false, []),
            _ => new QueryRow("e", 1L, """{"status":"open","rank":40}""", false, [])
        }).ToArray();
    }
}
