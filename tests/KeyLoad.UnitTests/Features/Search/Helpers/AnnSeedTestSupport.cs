using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Core.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

internal static class AnnSeedTestSupport
{
    internal const string Collection = "ann-seed-records";
    internal const string Field = "/embedding";
    internal const string FieldUse = "ann.seed.use";
    internal const string FieldRead = "ann.seed.read";
    internal const string FieldWrite = "ann.seed.write";
    internal const string Principal = "seed-reader";
    internal const string Owner = "seed-owner";
    internal const string Model = "seed-model";
    internal const string Version = "v1";
    internal const string ResourceGrant = Collection;
    internal const int Dimension = 3;

    internal static VectorSpace Space(DistanceMetric metric = DistanceMetric.Cosine, int dimension = Dimension,
        string id = "seed-space", string model = Model, string version = Version)
        => new(id, dimension, metric, model, version);

    internal static TestDatabase Create(int count = 3, DatabaseLimits? limits = null)
    {
        var database = new TestDatabase(limits);
        try
        {
            database.Configure(Collection, ResourceKind.Collection, fields:
                [new(Field, "embedding", FieldRead, FieldUse, FieldWrite)]);
            for (var start = 0; start < count; start += 64)
            {
                var end = Math.Min(count, start + 64);
                var mutations = new List<Mutation>((end - start) * 2);
                for (var index = start; index < end; index++)
                {
                    var id = Id(index);
                    mutations.Add(new PutDocument(Collection, id, "{}", Access: new(Owner)));
                    mutations.Add(new PutVector(Collection, id, Field,
                        ImmutableArray.Create((float)index + 0.25f, 1f, -0.5f), Space(), 1));
                }
                database.Commit([.. mutations]);
            }
            Persist(database, Principal, Capability.VectorSearch, [FieldUse], owner: Owner, restrictRows: true);
            return database;
        }
        catch (Exception)
        {
            database.Dispose();
            throw;
        }
    }

    internal static void Persist(TestDatabase database, string principalId, Capability capabilities,
        string[] fieldGrants, string? owner = null, bool restrictRows = false,
        bool revoked = false, DateTimeOffset? expiresAt = null, long policyEpoch = 1)
    {
        var principal = new PrincipalRecord(principalId, database.Partition.TenantId,
            capabilities == Capability.None ? [] : [new("database", ResourceGrant, capabilities)],
            [.. fieldGrants])
        {
            OwnerId = owner,
            RestrictRows = restrictRows,
            Revoked = revoked,
            ExpiresAt = expiresAt,
            PolicyEpoch = policyEpoch
        };
        database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal)).Get<PrincipalRecord>();
    }

    internal static AnnSeed Capture(TestDatabase database, string principalId = Principal,
        VectorSpace? space = null, AnnSeedOptions? options = null, ReadExecutionBudget? budget = null)
        => AnnSeedCollector.Capture(database.Database, principalId, database.Partition, Collection, Field,
            space ?? Space(), Microsoft.Extensions.Options.Options.Create(options ?? new()), budget ?? new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(database.Database.Limits)));

    internal static KeyLoadException CaptureFailure(TestDatabase database, string principalId,
        VectorSpace? space = null, AnnSeedOptions? options = null, ReadExecutionBudget? budget = null)
    {
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => Capture(database, principalId, space, options, budget));
        return failure;
    }

    internal static string Id(int index) => $"seed-{index:D5}";

    internal static void CommitVector(TestDatabase database, string id, ImmutableArray<float> values,
        VectorSpace? space = null, long revision = 1)
        => database.Commit(new PutVector(Collection, id, Field, values, space ?? Space(), revision));

}
