using System.Collections.Immutable;

namespace KeyLoad.UnitTests.Features.Search;

internal static class HybridQualityCorpus
{
    internal const string Collection = "hybrid-quality-documents";
    internal const string Graph = "hybrid-quality-links";
    internal const string TextField = "/body";
    internal const string VectorField = "/embedding";
    internal const string TextUseGrant = "hybrid-quality.text.use";
    internal const string VectorUseGrant = "hybrid-quality.vector.use";
    internal const string PrincipalId = "hybrid-quality-reader";
    internal const string RootId = "d31";
    internal const int ResultLimit = 32;
    internal const int FusionConstant = 60;
    internal const double BranchWeight = 1;
    internal const string ScoreProfile = "hybrid-quality-current-v1";
    internal const string CorpusScope = "hybrid-quality-fixed32-v1";
    internal const string StatisticsEpoch = "fixture-cut-v1";
    internal const string WindowId = "complete-observation";

    internal static VectorSpace Space { get; } = new("hybrid-quality-space", 4,
        DistanceMetric.Cosine, "fixed-fixture-embedding", "1");

    internal static ImmutableArray<string> AllIds { get; } =
    ["d00", "d01", "d02", "d03", "d04", "d05", "d06", "d07",
     "d08", "d09", "d10", "d11", "d12", "d13", "d14", "d15",
     "d16", "d17", "d18", "d19", "d20", "d21", "d22", "d23",
     "d24", "d25", "d26", "d27", "d28", "d29", "d30", "d31"];

    internal static ImmutableArray<HybridQualityDocument> Documents { get; } =
    [
        new("d00", "{\"body\":\"amber reactor signal\"}", [1f, 0f, 0f, 0f]),
        new("d01", "{\"body\":\"amber reactor fuel\"}", [0.9f, 0.1f, 0f, 0f]),
        new("d02", "{\"body\":\"amber signal coil\"}", [0.8f, 0.2f, 0f, 0f]),
        new("d03", "{\"body\":\"blue reactor coolant\"}", [0.7f, 0.3f, 0f, 0f]),
        new("d04", "{\"body\":\"signal coolant\"}", [0.6f, 0.4f, 0f, 0f]),
        new("d05", "{\"label\":\"body is missing\"}", [0.5f, 0.5f, 0f, 0f]),
        new("d06", "{\"body\":\"amber amber valve valve\"}", [0.4f, 0.6f, 0f, 0f]),
        new("d07", "{\"body\":\"repeated amber valve\"}", [0.3f, 0.7f, 0f, 0f]),
        new("d08", "{\"body\":\"синій реактор котушка\"}", [0f, 1f, 0f, 0f]),
        new("d09", "{\"body\":\"синій сигнал котушка\"}", [0f, 0.9f, 0.1f, 0f]),
        new("d10", "{\"body\":\"котушка двигун\"}", [0f, 0.8f, 0.2f, 0f]),
        new("d11", "{\"body\":\"реактор тепло\"}", [0f, 0.7f, 0.3f, 0f]),
        new("d12", "{\"body\":\"синій синій котушка\"}", [0f, 0.6f, 0.4f, 0f]),
        new("d13", "{\"body\":\"потік датчик\"}", [0f, 0.5f, 0.5f, 0f]),
        new("d14", "{\"body\":\"amber valve repeat\"}", [0f, 0.4f, 0.6f, 0f]),
        new("d15", "{\"body\":\"клапан сигнал\"}", [0f, 0.3f, 0.7f, 0f]),
        new("d16", "{\"body\":\"amber sensor field\"}", [0f, 0f, 1f, 0f]),
        new("d17", "{\"other\":\"text field absent\"}", [0f, 0f, 0.9f, 0.1f]),
        new("d18", "{\"body\":\"\"}", [0f, 0f, 0.8f, 0.2f]),
        new("d19", "{\"body\":\"amber amber sensor\"}", [0f, 0f, 0.7f, 0.3f]),
        new("d20", "{\"body\":\"shared ballast\"}", [0.5f, 0.5f, 0.5f, 0.5f]),
        new("d21", "{\"body\":\"shared ballast\"}", [0.5f, 0.5f, 0.5f, 0.5f]),
        new("d22", "{\"body\":\"shared compass\"}", [0.6f, 0.4f, 0.6f, 0.4f]),
        new("d23", "{\"body\":\"shared compass\"}", [0.4f, 0.6f, 0.4f, 0.6f]),
        new("d24", "{\"body\":\"система синій\"}", [0f, 0f, 0f, 1f]),
        new("d25", "{\"body\":\"reactor archive\"}", [0.1f, 0f, 0f, 0.9f]),
        new("d26", "{\"body\":\"valve archive\"}", [0.2f, 0f, 0f, 0.8f]),
        new("d27", "{\"body\":\"sensor archive\"}", [0.3f, 0f, 0f, 0.7f]),
        new("d28", "{\"body\":\"реактор архів\"}", [0.4f, 0f, 0f, 0.6f]),
        new("d29", "{\"body\":\"signal archive\"}", [0.5f, 0f, 0f, 0.5f]),
        new("d30", "{\"body\":\"archive root marker\"}", [0.6f, 0f, 0f, 0.4f]),
        new("d31", "{\"body\":\"archive root\"}", [0.7f, 0f, 0f, 0.3f])
    ];

    internal static ImmutableArray<HybridQualityQuery> Queries { get; } =
    [
        new("english", "amber reactor", [1f, 0f, 0f, 0f], AllIds,
            [3, 2, 2, 1, 1, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0,
             0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], true),
        new("ukrainian", "синій котушка", [0f, 1f, 0f, 0f], AllIds,
            [0, 0, 0, 0, 0, 0, 0, 0, 3, 2, 1, 1, 2, 0, 0, 0,
             0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0], true),
        new("repeated", "amber amber valve", [0.8f, 0.2f, 0f, 0f], AllIds,
            [1, 0, 1, 0, 0, 0, 3, 2, 0, 0, 0, 0, 0, 0, 1, 0,
             0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], true),
        new("missing-text", "quasarunlisted", [0.5f, 0.5f, 0.5f, 0.5f], AllIds,
            [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
             0, 0, 0, 0, 3, 2, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0], true),
        new("graph-selective", null, null, ["d00", "d08", "d16", "d24"],
            [3, 0, 0, 0, 0, 0, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0,
             1, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0], true),
        new("empty-allowlist", "amber", [1f, 0f, 0f, 0f], [],
            [3, 2, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
             0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], true)
    ];

    private static ImmutableArray<string> GraphTargets { get; } =
    ["d00", "d01", "d02", "d03", "d04", "d05", "d06", "d07",
     "d08", "d09", "d10", "d11", "d12", "d13", "d14", "d15",
     "d16", "d17", "d18", "d19", "d20", "d21", "d22", "d23",
     "d24", "d25", "d26", "d27", "d28", "d29", "d30"];

    internal static ImmutableArray<HybridQualityJudgment> Judgments(HybridQualityQuery query)
        => [.. AllIds.Select((id, index) => new HybridQualityJudgment(id, query.Grades[index]))];

    internal static void ValidateDefinitions()
    {
        if (Documents.Length != ResultLimit || AllIds.Length != Documents.Length
            || Queries.Length != 6 || Documents.Any(document => document.Embedding.Length != 4)
            || Queries.Any(query => query.Grades.Length != Documents.Length
                || query.Grades.Any(grade => grade is < 0 or > 3)
                || query.EligibleIds.Distinct(StringComparer.Ordinal).Count() != query.EligibleIds.Length
                || query.EligibleIds.Any(id => !AllIds.Contains(id, StringComparer.Ordinal))))
        {
            throw new InvalidOperationException("The frozen hybrid quality corpus shape changed.");
        }
        if (!Documents.Select(document => document.Id).SequenceEqual(AllIds, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("The fixed corpus ID order does not match the declared eligibility universe.");
        }
    }

    internal static void Seed(TestDatabase database)
    {
        database.Configure(Collection, ResourceKind.Collection, fields:
        [
            new(TextField, "text", RawUseGrant: TextUseGrant),
            new(VectorField, "vector", RawUseGrant: VectorUseGrant)
        ]);
        database.Configure(Graph, ResourceKind.Graph);
        var mutations = new List<Mutation>(Documents.Length * 2);
        foreach (var document in Documents)
        {
            mutations.Add(new PutDocument(Collection, document.Id, document.Json));
            mutations.Add(new PutVector(Collection, document.Id, VectorField, document.Embedding, Space, 1));
        }
        _ = database.Commit([.. mutations]);
        var source = new EntityRef(database.Partition, Collection, RootId);
        var edges = GraphTargets.Select((id, index) => (Mutation)new UpsertEdge(Graph,
            $"edge-{index:D2}", source, new(database.Partition, Collection, id), "related")).ToArray();
        _ = database.Commit(edges);
        PersistReader(database);
    }

    internal static void PersistReader(TestDatabase database)
    {
        var principal = new PrincipalRecord(PrincipalId, database.Partition.TenantId,
        [
            new("database", Collection, Capability.Query | Capability.DocumentsRead | Capability.VectorSearch),
            new("database", Graph, Capability.GraphRead)
        ], [TextUseGrant, VectorUseGrant]);
        _ = database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal))
            .Get<PrincipalRecord>();
    }

    internal static EntityRef Root(TestDatabase database)
        => new(database.Partition, Collection, RootId);

    internal static SearchRequest SearchRequest(TestDatabase database, HybridQualityQuery query)
        => new(database.Partition, Collection,
            query.Text is null ? null : TextField, query.Text,
            query.Vector is null ? null : VectorField, query.Vector, query.Vector is null ? null : Space,
            ResultLimit, BranchWeight, BranchWeight, FusionConstant, query.EligibleIds);

    internal static GraphSearchRequest GraphRequest(TestDatabase database, HybridQualityQuery query)
        => new(1, SearchRequest(database, query), Retriever: query.IncludeGraph
            ? new(new(Graph, [Root(database)], MaxDepth: 1, MaxVertices: 32, MaxEdges: 64), BranchWeight)
            : null);
}
