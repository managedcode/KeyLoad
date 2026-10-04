using System.Collections.Immutable;

namespace KeyLoad.UnitTests.Features.Search;

internal static class FilteredSearchTestSupport
{
    internal const string Collection = "filtered-search-records";
    internal const string TextField = "/text";
    internal const string VectorField = "/embedding";
    internal const string TextUseGrant = "filtered.search.text.use";
    internal const string VectorUseGrant = "filtered.search.vector.use";
    internal const string Model = "filtered-model";
    internal const string Version = "v1";
    internal const string PartitionKey = "customer-1";
    internal const int Dimension = 2;
    internal const int Revision = 1;
    internal const int FusionConstant = 13;
    internal const double Tolerance = 0.0000000000005;
    internal static VectorSpace Space { get; } = new("filtered-space", Dimension, DistanceMetric.Cosine, Model, Version);

    internal static TestDatabase Create(DatabaseLimits? limits = null)
    {
        var database = new TestDatabase(limits);
        try
        {
            database.Configure(Collection, ResourceKind.Collection, fields:
            [
                new(TextField, "text", "filtered.search.text.read", TextUseGrant, "filtered.search.text.write"),
                new(VectorField, "embedding", "filtered.search.vector.read", VectorUseGrant, "filtered.search.vector.write")
            ]);
            return database;
        }
        catch (Exception)
        {
            database.Dispose();
            throw;
        }
    }

    internal static void AddCorpus(TestDatabase database)
    {
        database.Commit(
            new PutDocument(Collection, "a", "{\"text\":\"alpha alpha\"}"),
            new PutDocument(Collection, "b", "{\"text\":\"alpha beta\"}"),
            new PutDocument(Collection, "c", "{\"text\":\"beta\"}"),
            new PutDocument(Collection, "d", "{\"text\":\"gamma\"}"),
            new PutVector(Collection, "a", VectorField, [1, 0], Space, Revision),
            new PutVector(Collection, "b", VectorField, [0, 1], Space, Revision),
            new PutVector(Collection, "c", VectorField, [-1, 0], Space, Revision),
            new PutVector(Collection, "d", VectorField, [0, 1], Space, Revision));
    }

    internal static SearchRequest Request(ImmutableArray<string>? allowedIds = null, bool hybrid = true)
        => hybrid
            ? new SearchRequest(new("tenant", "database", "orders", PartitionKey), Collection,
                TextField, "alpha", VectorField, [1, 0], Space, Limit: 10, FusionConstant: FusionConstant,
                AllowedIds: allowedIds)
            : new SearchRequest(new("tenant", "database", "orders", PartitionKey), Collection,
                TextField, "alpha", AllowedIds: allowedIds);

    internal static void PersistReader(TestDatabase database, string id, Capability capabilities,
        string[] fieldGrants, string? owner = null, bool restrictRows = false, bool revoked = false)
    {
        var principal = new PrincipalRecord(id, database.Partition.TenantId,
            [new(database.Partition.DatabaseId, Collection,
                Capability.Query | Capability.DocumentsRead | capabilities)],
            [.. fieldGrants])
        {
            OwnerId = owner,
            RestrictRows = restrictRows,
            Revoked = revoked
        };
        database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal)).Get<PrincipalRecord>();
    }
}
