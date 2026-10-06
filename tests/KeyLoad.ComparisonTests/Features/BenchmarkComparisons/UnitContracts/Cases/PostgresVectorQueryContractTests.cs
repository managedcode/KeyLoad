using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;
using Npgsql;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>Checks actual Npgsql command contracts; native execution remains a separate Aspire gate.</summary>
internal sealed class PostgresVectorQueryContractTests
{
    [Test]
    [Arguments(VectorQueryMode.Plain, "FROM documents ORDER BY")]
    [Arguments(VectorQueryMode.Filtered, "WHERE vector_number % 100 = 0")]
    [Arguments(VectorQueryMode.Mixed, "WHERE vector_number % 10 <> 9")]
    public async Task ExactCommandsRankBoundaryTiesBeforeLimitAndKeepNativeEligibility(VectorQueryMode mode, string predicate)
    {
        using var search = new NpgsqlCommand();
        using var explain = new NpgsqlCommand();
        PostgresNativeVectorQueries.SelectSearch(search, VectorIndexKind.Exact, mode);
        PostgresNativeVectorQueries.SelectExplain(explain, VectorIndexKind.Exact, mode);
        await Assert.That(search.CommandText.Contains(predicate, StringComparison.Ordinal)).IsTrue();
        await Assert.That(explain.CommandText.Contains(predicate, StringComparison.Ordinal)).IsTrue();
        await Assert.That(search.CommandText.Contains("ORDER BY embedding <=> $1::vector,id LIMIT $2", StringComparison.Ordinal)).IsTrue();
        await Assert.That(explain.CommandText.Contains("ORDER BY embedding <=> $1::vector,id LIMIT 10", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    [Arguments(VectorIndexKind.Hnsw)]
    [Arguments(VectorIndexKind.IvfFlat)]
    public async Task AnnCommandsKeepDistanceOnlyCandidateOrderForNativeIndexPlans(VectorIndexKind kind)
    {
        using var search = new NpgsqlCommand();
        using var explain = new NpgsqlCommand();
        PostgresNativeVectorQueries.SelectSearch(search, kind, VectorQueryMode.Filtered);
        PostgresNativeVectorQueries.SelectExplain(explain, kind, VectorQueryMode.Filtered);
        await Assert.That(search.CommandText.Contains("ORDER BY embedding <=> $1::vector LIMIT $2", StringComparison.Ordinal)).IsTrue();
        await Assert.That(explain.CommandText.Contains("ORDER BY embedding <=> $1::vector LIMIT 10", StringComparison.Ordinal)).IsTrue();
        await Assert.That(search.CommandText.EndsWith("ORDER BY distance,id", StringComparison.Ordinal)).IsTrue();
        await Assert.That(explain.CommandText.EndsWith("ORDER BY distance,id", StringComparison.Ordinal)).IsTrue();
    }
}
