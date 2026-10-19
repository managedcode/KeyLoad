using System.Globalization;
using Npgsql;
using NpgsqlTypes;

namespace KeyLoad.Comparisons.Targets;

internal static class PostgresVectorIndex
{
    private const string IndexDefinitionSql = "SELECT indexdef FROM pg_indexes WHERE schemaname=current_schema() AND indexname=$1";
    private const string AnalyzeSql = "ANALYZE documents";
    private const string CreateHnswSql = "CREATE INDEX vectors_hnsw_idx ON documents USING hnsw (embedding vector_cosine_ops) WITH (m = 16, ef_construction = 200)";
    private const string CreateIvf100kSql = "CREATE INDEX vectors_ivfflat_idx ON documents USING ivfflat (embedding vector_cosine_ops) WITH (lists = 316)";
    private const string CreateIvf1mSql = "CREATE INDEX vectors_ivfflat_idx ON documents USING ivfflat (embedding vector_cosine_ops) WITH (lists = 1000)";
    private const string HnswSettingsSql = "SET hnsw.ef_search = 200; SET hnsw.iterative_scan = strict_order";
    private const string IvfProbeSql = "SELECT pg_catalog.set_config('ivfflat.probes',$1,false)";
    private const string IvfScanSql = "SET ivfflat.iterative_scan = relaxed_order";
    private const string ForceIndexSql = "SET enable_seqscan = off";
    private const string VectorParamM = "m";
    private const string VectorParamEfConstruction = "efConstruction";
    private const string VectorParamEfSearch = "efSearch";
    private const string VectorParamLists = "lists";
    private const string VectorParamProbes = "probes";
    private const string VectorParamIterativeScan = "iterativeScan";

    internal static async Task<VectorIndexReceipt> BuildAsync(NpgsqlDataSource source, VectorComparisonProfile profile, ComparisonExecutionOptions execution, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (var analyze = connection.CreateCommand())
        {
            analyze.CommandText = AnalyzeSql;
            await analyze.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        if (profile.IndexKind == VectorIndexKind.Exact)
        {
            return new(VectorIndexKind.Exact, PostgresVectorIndexValues.NoANNIndexExactCosine, new Dictionary<string, string>(), PostgresVectorIndexValues.FirstIndex);
        }

        var parameters = Parameters(profile);
        var timer = new ComparisonElapsedMeasurement(timeProvider);
        await CreateIndexAsync(connection, profile, execution, cancellationToken).ConfigureAwait(false);
        timer.Stop();
        await using var metadata = connection.CreateCommand();
        metadata.CommandText = IndexDefinitionSql;
        metadata.Parameters.AddWithValue(profile.IndexKind == VectorIndexKind.Hnsw ? PostgresVectorIndexValues.VectorsHnswIdx : PostgresVectorIndexValues.VectorsIvfflatIdx);
        var definition = await metadata.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        if (string.IsNullOrWhiteSpace(definition))
        {
            throw new InvalidDataException(PostgresVectorIndexValues.TheRequestedPostgreSQLVectorIndexIs);
        }
        return new(profile.IndexKind, definition, parameters, timer.Elapsed.TotalMilliseconds);
    }

    internal static async Task<IReadOnlyList<VectorNeighbor>> SearchAsync(NpgsqlDataSource source,
        VectorComparisonProfile profile, ReadOnlyMemory<float> query, int topK, VectorQueryMode mode,
        int vectorComponentBuilderCapacity, CancellationToken cancellationToken)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await ConfigureSearchAsync(connection, profile, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        PostgresVectorQueries.SelectSearch(command, profile.IndexKind, mode);
        command.Parameters.AddWithValue(PostgresVectorStorage.VectorLiteral(query.Span, vectorComponentBuilderCapacity));
        command.Parameters.AddWithValue(NpgsqlDbType.Integer, topK);
        var results = new List<VectorNeighbor>(topK);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new(reader.GetString(PostgresVectorIndexValues.FirstIndex), reader.GetDouble(PostgresVectorIndexValues.SingleElementOffset)));
        }
        return results;
    }

    internal static async Task<string> ExplainAsync(NpgsqlDataSource source, VectorComparisonProfile profile,
        ReadOnlyMemory<float> query, VectorQueryMode mode, int vectorComponentBuilderCapacity, CancellationToken cancellationToken)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await ConfigureSearchAsync(connection, profile, cancellationToken).ConfigureAwait(false);
        if (profile.IndexKind is VectorIndexKind.Hnsw or VectorIndexKind.IvfFlat)
        {
            await using var force = connection.CreateCommand();
            force.CommandText = ForceIndexSql;
            await force.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await using var command = connection.CreateCommand();
        PostgresVectorQueries.SelectExplain(command, profile.IndexKind, mode);
        command.Parameters.AddWithValue(PostgresVectorStorage.VectorLiteral(query.Span, vectorComponentBuilderCapacity));
        var lines = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            lines.Add(reader.GetString(PostgresVectorIndexValues.FirstIndex));
        }
        var plan = string.Join(PostgresVectorIndexValues.LineBreakCharacter, lines);
        var expectedIndex = profile.IndexKind == VectorIndexKind.Hnsw ? PostgresVectorIndexValues.VectorsHnswIdx : PostgresVectorIndexValues.VectorsIvfflatIdx;
        if (profile.IndexKind is VectorIndexKind.Hnsw or VectorIndexKind.IvfFlat
            && !plan.Contains(expectedIndex, StringComparison.Ordinal))
        {
            throw new InvalidDataException(PostgresVectorIndexValues.PostgreSQLPlannerDidNotSelectThe);
        }
        return plan;
    }

    private static Dictionary<string, string> Parameters(VectorComparisonProfile profile)
    {
        if (profile.IndexKind == VectorIndexKind.Hnsw)
        {
            return new()
            {
                [VectorParamM] = PostgresVectorIndexValues.HnswNeighborsSetting,
                [VectorParamEfConstruction] = PostgresVectorIndexValues.HnswEffortSetting,
                [VectorParamEfSearch] = PostgresVectorIndexValues.HnswEffortSetting
            };
        }
        var lists = Math.Max(PostgresVectorIndexValues.SingleElementOffset, (int)Math.Sqrt(profile.RecordCount));
        var probes = Math.Min(lists, (int)Math.Ceiling(Math.Sqrt(lists)) * PostgresVectorIndexValues.IvfProbeExpansionFactor);
        return new()
        {
            [VectorParamLists] = lists.ToString(CultureInfo.InvariantCulture),
            [VectorParamProbes] = probes.ToString(CultureInfo.InvariantCulture),
            [VectorParamIterativeScan] = PostgresVectorIndexValues.RelaxedOrder
        };
    }

    private static async Task CreateIndexAsync(NpgsqlConnection connection, VectorComparisonProfile profile,
        ComparisonExecutionOptions execution, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        if (profile.IndexKind == VectorIndexKind.Hnsw)
        {
            command.CommandText = CreateHnswSql;
        }
        else if (profile.IndexKind != VectorIndexKind.IvfFlat)
        {
            throw new NotSupportedException(PostgresVectorIndexValues.PostgreSQLDoesNotExposeTheSelected);
        }
        else if (profile.RecordCount == PostgresVectorIndexValues.Scale100kCount)
        {
            command.CommandText = CreateIvf100kSql;
        }
        else
        {
            command.CommandText = CreateIvf1mSql;
        }
        command.CommandTimeout = checked((int)Math.Ceiling(execution.IndexBuildTimeout.TotalSeconds));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ConfigureSearchAsync(NpgsqlConnection connection, VectorComparisonProfile profile,
        CancellationToken cancellationToken)
    {
        if (profile.IndexKind == VectorIndexKind.Hnsw)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = HnswSettingsSql;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (profile.IndexKind == VectorIndexKind.IvfFlat)
        {
            var lists = Math.Max(PostgresVectorIndexValues.SingleElementOffset, (int)Math.Sqrt(profile.RecordCount));
            var probes = Math.Min(lists, (int)Math.Ceiling(Math.Sqrt(lists)) * PostgresVectorIndexValues.IvfProbeExpansionFactor);
            await using (var probe = connection.CreateCommand())
            {
                probe.CommandText = IvfProbeSql;
                probe.Parameters.AddWithValue(NpgsqlDbType.Text, probes.ToString(CultureInfo.InvariantCulture));
                await probe.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            }
            await using var scan = connection.CreateCommand();
            scan.CommandText = IvfScanSql;
            await scan.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        if (profile.IndexKind is VectorIndexKind.Hnsw or VectorIndexKind.IvfFlat)
        {
            await using var force = connection.CreateCommand();
            force.CommandText = ForceIndexSql;
            await force.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
