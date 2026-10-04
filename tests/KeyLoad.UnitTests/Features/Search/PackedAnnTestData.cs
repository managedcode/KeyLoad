using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.Search;

internal static class PackedAnnTestData
{
    internal const string Collection = "packed-ann-records";
    internal const string Principal = "root";
    internal const string Model = "packed-ann-model";
    internal const string Version = "1";
    internal const string IdPrefix = "record-";
    internal const string FieldPrefix = "/embedding-";
    internal const string Wildcard = "*";
    internal const int DocumentRevision = 1;
    internal const int SeedBatchSize = 64;
    internal const int QualityRecordCount = 10_000;
    internal const int QualityQueryCount = 100;
    internal const int QualityDimension = 16;
    internal const int TopK = 10;
    internal const ulong CorpusSeed = 0x4B45594C4F414431UL;

    internal static void Configure(TestDatabase database)
    {
        database.Configure(Collection, ResourceKind.Collection);
        var root = database.Store.Read(view => view.GetRecord<PrincipalRecord>(KeySpace.Principal(Principal)));
        var resource = database.Store.Read(view => view.GetRecord<ResourceDefinition>(KeySpace.Resource(
            database.Partition.TenantId, database.Partition.DatabaseId, Collection)));
        if (root is null || root.Id != Principal
            || !root.Grants.Any(grant => grant.Database == Wildcard && grant.Resource == Wildcard
                && grant.Capabilities == Capability.All)
            || !root.FieldGrants.Contains(Wildcard, StringComparer.Ordinal)
            || resource?.Kind != ResourceKind.Collection)
        {
            throw new InvalidOperationException("The packed ANN fixture requires persisted root and collection records.");
        }
    }

    internal static VectorSpace Space(DistanceMetric metric, int dimension)
        => new($"ann-{metric}", dimension, metric, Model, Version);

    internal static string Field(DistanceMetric metric) => FieldPrefix + metric;

    internal static void Seed(TestDatabase database, int count, int dimension, params DistanceMetric[] metrics)
    {
        var spaces = metrics.Select(metric => (Metric: metric, Space: Space(metric, dimension), Field: Field(metric))).ToArray();
        for (var start = 0; start < count; start += SeedBatchSize)
        {
            var end = Math.Min(count, start + SeedBatchSize);
            var mutations = new List<Mutation>((end - start) * (metrics.Length + 1));
            for (var ordinal = start; ordinal < end; ordinal++)
            {
                var id = DocumentId(ordinal);
                mutations.Add(new PutDocument(Collection, id, "{}"));
                var values = Vector(ordinal, dimension, CorpusSeed);
                foreach (var entry in spaces)
                {
                    mutations.Add(new PutVector(Collection, id, entry.Field, ImmutableArray.CreateRange(values),
                        entry.Space, DocumentRevision));
                }
            }
            database.Commit([.. mutations]);
        }
    }

    internal static VectorRecord[] Load(TestDatabase database, DistanceMetric metric)
    {
        var principal = database.Store.Read(view => view.GetRecord<PrincipalRecord>(KeySpace.Principal(Principal)))
            ?? throw new InvalidOperationException("The packed ANN root principal is missing.");
        if (principal.Id != Principal)
        {
            throw new InvalidOperationException("The packed ANN read must use its persisted principal.");
        }
        return database.Database.WithVectors(Principal, database.Partition, Collection, Field(metric),
            (_, _, pairs) => pairs.Select(pair => pair.Vector).OrderBy(record => record.DocumentId, StringComparer.Ordinal).ToArray());
    }

    internal static string DocumentId(int ordinal) => IdPrefix + ordinal.ToString("D5", System.Globalization.CultureInfo.InvariantCulture);

    internal static float[] Vector(int ordinal, int dimension, ulong seed)
    {
        var state = seed ^ (unchecked((ulong)ordinal + 1) * 0x9E3779B97F4A7C15UL);
        var values = new float[dimension];
        for (var component = 0; component < values.Length; component++)
        {
            var sample = Next(ref state);
            values[component] = (float)(((sample >> 40) / 8_388_608d) - 1d);
        }
        return values;
    }

    private static ulong Next(ref ulong state)
    {
        state += 0x9E3779B97F4A7C15UL;
        var value = state;
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }
}
