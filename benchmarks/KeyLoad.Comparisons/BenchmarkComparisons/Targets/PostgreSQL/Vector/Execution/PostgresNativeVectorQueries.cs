using Npgsql;

namespace KeyLoad.Comparisons.Targets;

internal static class PostgresNativeVectorQueries
{
    private const string SearchExactPlainSql = "SELECT id,embedding <=> $1::vector AS distance FROM documents ORDER BY embedding <=> $1::vector,id LIMIT $2";
    private const string SearchExactFilteredSql = "SELECT id,embedding <=> $1::vector AS distance FROM documents WHERE vector_number % 100 = 0 ORDER BY embedding <=> $1::vector,id LIMIT $2";
    private const string SearchExactMixedSql = "SELECT id,embedding <=> $1::vector AS distance FROM documents WHERE vector_number % 10 <> 9 ORDER BY embedding <=> $1::vector,id LIMIT $2";
    private const string ExplainExactPlainSql = "EXPLAIN (FORMAT TEXT) SELECT id,embedding <=> $1::vector AS distance FROM documents ORDER BY embedding <=> $1::vector,id LIMIT 10";
    private const string ExplainExactFilteredSql = "EXPLAIN (FORMAT TEXT) SELECT id,embedding <=> $1::vector AS distance FROM documents WHERE vector_number % 100 = 0 ORDER BY embedding <=> $1::vector,id LIMIT 10";
    private const string ExplainExactMixedSql = "EXPLAIN (FORMAT TEXT) SELECT id,embedding <=> $1::vector AS distance FROM documents WHERE vector_number % 10 <> 9 ORDER BY embedding <=> $1::vector,id LIMIT 10";
    private const string SearchPlainSql = "SELECT id,distance FROM (SELECT id,embedding <=> $1::vector AS distance FROM documents ORDER BY embedding <=> $1::vector LIMIT $2) AS candidates ORDER BY distance,id";
    private const string SearchFilteredSql = "SELECT id,distance FROM (SELECT id,embedding <=> $1::vector AS distance FROM documents WHERE vector_number % 100 = 0 ORDER BY embedding <=> $1::vector LIMIT $2) AS candidates ORDER BY distance,id";
    private const string SearchMixedSql = "SELECT id,distance FROM (SELECT id,embedding <=> $1::vector AS distance FROM documents WHERE vector_number % 10 <> 9 ORDER BY embedding <=> $1::vector LIMIT $2) AS candidates ORDER BY distance,id";
    private const string ExplainPlainSql = "EXPLAIN (FORMAT TEXT) SELECT id,distance FROM (SELECT id,embedding <=> $1::vector AS distance FROM documents ORDER BY embedding <=> $1::vector LIMIT 10) AS candidates ORDER BY distance,id";
    private const string ExplainFilteredSql = "EXPLAIN (FORMAT TEXT) SELECT id,distance FROM (SELECT id,embedding <=> $1::vector AS distance FROM documents WHERE vector_number % 100 = 0 ORDER BY embedding <=> $1::vector LIMIT 10) AS candidates ORDER BY distance,id";
    private const string ExplainMixedSql = "EXPLAIN (FORMAT TEXT) SELECT id,distance FROM (SELECT id,embedding <=> $1::vector AS distance FROM documents WHERE vector_number % 10 <> 9 ORDER BY embedding <=> $1::vector LIMIT 10) AS candidates ORDER BY distance,id";

    internal static void SelectSearch(NpgsqlCommand command, VectorIndexKind indexKind, VectorQueryMode mode)
    {
        switch ((indexKind == VectorIndexKind.Exact, mode))
        {
            case (true, VectorQueryMode.Plain):
                command.CommandText = SearchExactPlainSql;
                break;
            case (true, VectorQueryMode.Filtered):
                command.CommandText = SearchExactFilteredSql;
                break;
            case (true, VectorQueryMode.Mixed):
                command.CommandText = SearchExactMixedSql;
                break;
            case (false, VectorQueryMode.Plain):
                command.CommandText = SearchPlainSql;
                break;
            case (false, VectorQueryMode.Filtered):
                command.CommandText = SearchFilteredSql;
                break;
            case (false, VectorQueryMode.Mixed):
                command.CommandText = SearchMixedSql;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode));
        }
    }

    internal static void SelectExplain(NpgsqlCommand command, VectorIndexKind indexKind, VectorQueryMode mode)
    {
        switch ((indexKind == VectorIndexKind.Exact, mode))
        {
            case (true, VectorQueryMode.Plain):
                command.CommandText = ExplainExactPlainSql;
                break;
            case (true, VectorQueryMode.Filtered):
                command.CommandText = ExplainExactFilteredSql;
                break;
            case (true, VectorQueryMode.Mixed):
                command.CommandText = ExplainExactMixedSql;
                break;
            case (false, VectorQueryMode.Plain):
                command.CommandText = ExplainPlainSql;
                break;
            case (false, VectorQueryMode.Filtered):
                command.CommandText = ExplainFilteredSql;
                break;
            case (false, VectorQueryMode.Mixed):
                command.CommandText = ExplainMixedSql;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode));
        }
    }
}
