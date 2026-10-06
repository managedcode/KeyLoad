using System.Security.Cryptography;
using System.Text;
using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class NativeComparisonSerializationProjectionTests
{
    private const string Table = "vectors";
    private const string RecordId = "v000000042";
    private const string VectorText = "[0,0.5,-1.25,128]";
    private const string BatchGolden = "CREATE ONLY vectors:v000000042 SET number = 42, embedding = [0,0.5,-1.25,128], payload = \"payload\";"
        + "CREATE ONLY vectors:v000000043 SET number = 43, embedding = [0,0.5,-1.25,128], payload = \"other\";";

    [Test]
    public async Task ActualNativeBuildersReserveTheConfiguredLowerCapacityAndOriginalDefaults()
    {
        const int RecordCount = 2;
        const int ComponentCount = 4;
        var lower = CreateLowerPolicy().Value;
        var defaults = new OptionsManager<NativeComparisonSerializationOptions>(new OptionsFactory<NativeComparisonSerializationOptions>(
            [], [], [new NativeComparisonSerializationOptionsValidator()])).Value;
        await Assert.That(SurrealDbVectorProtocol.CreateBatchBuilder(RecordCount, lower.SurrealDbBatchRecordBuilderCapacity).Capacity).IsEqualTo(2);
        await Assert.That(SurrealDbVectorProtocol.CreateBatchBuilder(RecordCount, defaults.SurrealDbBatchRecordBuilderCapacity).Capacity).IsEqualTo(24000);
        await Assert.That(SurrealDbVectorProtocol.CreateVectorBuilder(ComponentCount, lower.SurrealDbVectorComponentBuilderCapacity).Capacity).IsEqualTo(6);
        await Assert.That(SurrealDbVectorProtocol.CreateVectorBuilder(ComponentCount, defaults.SurrealDbVectorComponentBuilderCapacity).Capacity).IsEqualTo(50);
        await Assert.That(PostgresNativeVectorStorage.CreateVectorBuilder(ComponentCount, lower.PostgresVectorComponentBuilderCapacity).Capacity).IsEqualTo(6);
        await Assert.That(PostgresNativeVectorStorage.CreateVectorBuilder(ComponentCount, defaults.PostgresVectorComponentBuilderCapacity).Capacity).IsEqualTo(58);
    }

    [Test]
    public async Task LowerReservationsRetainExactNativeSqlAndCanonicalVectorDigest()
    {
        var native = CreateLowerPolicy();
        float[] vector = [0f, 0.5f, -1.25f, 128f];
        List<VectorDocument> batch = [new(42, RecordId, vector, "payload"), new(43, "v000000043", vector, "other")];
        var sql = SurrealDbVectorProtocol.CreateBatchSql(Table, batch, native.Value.SurrealDbBatchRecordBuilderCapacity,
            native.Value.SurrealDbVectorComponentBuilderCapacity);
        await Assert.That(sql).IsEqualTo(BatchGolden);
        await Assert.That(Hash(sql)).IsEqualTo(Hash(BatchGolden));
        var postgres = PostgresNativeVectorStorage.VectorLiteral(vector, native.Value.PostgresVectorComponentBuilderCapacity);
        await Assert.That(postgres).IsEqualTo(VectorText);
        // These little-endian float bytes are reconstructed independently of either SQL formatter.
        var expectedDigest = Convert.ToHexStringLower(SHA256.HashData(Convert.FromHexString("000000000000003f0000a0bf00000043")));
        await Assert.That(VectorComparisonCorpus.HashVector(vector)).IsEqualTo(expectedDigest);
        await Assert.That(SurrealDbVectorProtocol.UpdateSql(Table, RecordId, vector, native.Value.SurrealDbVectorComponentBuilderCapacity))
            .IsEqualTo("UPDATE vectors:v000000042 SET embedding = [0,0.5,-1.25,128] RETURN AFTER;");
        await Assert.That(sql).IsEqualTo(SurrealDbVectorProtocol.CreateBatchSql(Table, batch, 12000, 12));
        await Assert.That(postgres).IsEqualTo(PostgresNativeVectorStorage.VectorLiteral(vector, 14));
    }

    [Test]
    [Arguments(VectorQueryMode.Plain, "")]
    [Arguments(VectorQueryMode.Filtered, " AND number % 100 = 0")]
    [Arguments(VectorQueryMode.Mixed, " AND number % 10 != 9")]
    public async Task LowerReservationKeepsExactAndHnswOperatorsFiltersOrderingAndExplain(VectorQueryMode mode, string filter)
    {
        var capacity = CreateLowerPolicy().Value.SurrealDbVectorComponentBuilderCapacity;
        float[] vector = [0f, 0.5f, -1.25f, 128f];
        var exact = "SELECT id, vector::distance::knn() AS distance FROM vectors WHERE embedding <|10, COSINE|> "
            + VectorText + filter + " ORDER BY distance, id LIMIT 10;";
        var hnsw = "SELECT id, vector::distance::knn() AS distance FROM vectors WHERE embedding <|10, 200|> "
            + VectorText + filter + " ORDER BY distance, id LIMIT 10;";
        await Assert.That(SurrealDbVectorProtocol.SearchSql(Table, vector, 10, mode, VectorIndexKind.Exact, 200, capacity)).IsEqualTo(exact);
        await Assert.That(SurrealDbVectorProtocol.SearchSql(Table, vector, 10, mode, VectorIndexKind.Hnsw, 200, capacity)).IsEqualTo(hnsw);
        await Assert.That(SurrealDbVectorProtocol.ExplainSql(Table, vector, mode, VectorIndexKind.Exact, 200, capacity)).IsEqualTo("EXPLAIN FORMAT JSON " + exact);
        await Assert.That(SurrealDbVectorProtocol.ExplainSql(Table, vector, mode, VectorIndexKind.Hnsw, 200, capacity)).IsEqualTo("EXPLAIN FORMAT JSON " + hnsw);
    }

    [Test]
    [Arguments(float.NaN)]
    [Arguments(float.PositiveInfinity)]
    [Arguments(float.NegativeInfinity)]
    public async Task LowerReservationRetainsFiniteVectorRejection(float invalid)
    {
        var capacity = CreateLowerPolicy().Value.SurrealDbVectorComponentBuilderCapacity;
        await Assert.That(() => SurrealDbVectorProtocol.UpdateSql(Table, RecordId, new float[] { invalid }, capacity))
            .ThrowsExactly<InvalidDataException>();
    }

    private static OptionsManager<NativeComparisonSerializationOptions> CreateLowerPolicy()
        => new(new OptionsFactory<NativeComparisonSerializationOptions>(
            [new ConfigureNamedOptions<NativeComparisonSerializationOptions>(Options.DefaultName, settings =>
            {
                settings.SurrealDbBatchRecordBuilderCapacity = 1;
                settings.SurrealDbVectorComponentBuilderCapacity = 1;
                settings.PostgresVectorComponentBuilderCapacity = 1;
            })], [], [new NativeComparisonSerializationOptionsValidator()]));

    private static string Hash(string sql) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sql)));
}
