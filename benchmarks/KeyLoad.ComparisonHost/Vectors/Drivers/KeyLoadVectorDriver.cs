using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.ComparisonHost.Deployment;

namespace KeyLoad.ComparisonHost.Vectors;

/// <summary>
/// KeyLoad through its SDK: each vector is a <see cref="PutDocument"/> plus a <see cref="PutVector"/> on that document in the
/// same atomic commit (a batch is one commit), and the nearest neighbours come from <c>SearchAsync</c> with only a vector
/// branch, which is an exact cosine scan of the collection.
/// </summary>
internal sealed class KeyLoadVectorDriver(BenchmarkConnection connection) : IVectorDriver
{
    private const string Collection = "vectors";
    private const string Field = "/embedding";
    private const string SpaceId = "benchmark";
    private const string Model = "seeded-float32";
    private const string ModelVersion = "1";
    private const long NewDocument = 0;
    private const long FirstRevision = 1;
    private const int MutationsPerVector = 2;
    private const string NotPrepared = "Prepare the KeyLoad vector collection before writing or searching it.";
    private readonly KeyLoadDatabase database = new(connection);
    private VectorSpace? space;

    public int BatchSize => 100;

    private VectorSpace Space => space ?? throw new InvalidOperationException(NotPrepared);

    public Task PrepareAsync(int dimensions, CancellationToken token)
    {
        space = new(SpaceId, dimensions, DistanceMetric.Cosine, Model, ModelVersion);
        return database.ConfigureAsync(database.Resource(Collection, ResourceKind.Collection), token);
    }

    public async Task InsertAsync(IReadOnlyList<VectorRecord> vectors, CancellationToken token)
    {
        var current = Space;
        var mutations = ImmutableArray.CreateBuilder<Mutation>(vectors.Count * MutationsPerVector);
        foreach (var vector in vectors)
        {
            mutations.Add(new PutDocument(Collection, vector.Id, JsonSerializer.Serialize(new { id = vector.Id }), NewDocument));
            mutations.Add(new PutVector(Collection, vector.Id, Field, ImmutableArray.Create<float>(vector.Values), current, FirstRevision));
        }
        _ = await database.CommitAsync(mutations.MoveToImmutable(), token);
    }

    public async Task<IReadOnlyList<string>> NearestAsync(float[] query, int limit, CancellationToken token)
    {
        var request = new SearchRequest(database.Partition, Collection, VectorField: Field, Vector: ImmutableArray.Create<float>(query),
            Space: Space, Limit: limit);
        var found = KeyLoadDatabase.Require(await database.Client.SearchAsync(request, token), nameof(NearestAsync));
        return [.. found.Select(item => item.Document.Reference.Id)];
    }

    public ValueTask DisposeAsync() => database.DisposeAsync();
}
