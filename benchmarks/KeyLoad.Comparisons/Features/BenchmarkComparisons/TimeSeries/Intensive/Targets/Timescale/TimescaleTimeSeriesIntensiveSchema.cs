using System.Globalization;
using Npgsql;
using NpgsqlTypes;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimescaleTimeSeriesIntensiveSchema
{
    private enum InstallStage { Tables, Append, Read, Aggregate, Windows }

    private const string SchemaToken = "__KEYLOAD_SCHEMA__";
    private const string DdlSearchPathSql = "SELECT set_config('search_path', $1, true)";
    private const string DdlSearchPathSuffix = ", pg_catalog, pg_temp";
    private const string SetDdlSql = "SELECT set_config('keyload.tsi_install_sql', $1, true)";
    private const string ExecuteDdlSql = "DO $$ BEGIN EXECUTE current_setting('keyload.tsi_install_sql'); END $$";
    private const string CounterSql = """
        CREATE TABLE "__KEYLOAD_SCHEMA__".series_counter(
            set_name text NOT NULL CHECK (octet_length(convert_to(set_name, 'UTF8')) BETWEEN 1 AND 256),
            series_id text NOT NULL CHECK (octet_length(convert_to(series_id, 'UTF8')) BETWEEN 1 AND 256),
            last_sequence int8 NOT NULL CHECK (last_sequence >= 0), PRIMARY KEY(set_name, series_id));
        """;
    private const string SampleColumnsSql = """
            set_name text NOT NULL CHECK (octet_length(convert_to(set_name, 'UTF8')) BETWEEN 1 AND 256),
            series_id text NOT NULL CHECK (octet_length(convert_to(series_id, 'UTF8')) BETWEEN 1 AND 256),
            event_id text NOT NULL CHECK (octet_length(convert_to(event_id, 'UTF8')) BETWEEN 1 AND 256),
            sample_time timestamptz NOT NULL CHECK (isfinite(sample_time)
                AND sample_time >= TIMESTAMPTZ '0001-01-01 00:00:00.000001+00'
                AND sample_time <= TIMESTAMPTZ '9999-12-31 23:59:59.999999+00'),
            sample_value float8 NOT NULL CHECK (sample_value > '-Infinity'::float8 AND sample_value < 'Infinity'::float8),
            sample_sequence int8 NOT NULL CHECK (sample_sequence > 0),
            tags_json jsonb NOT NULL CHECK (jsonb_typeof(tags_json) = 'object'
                AND octet_length(convert_to(tags_json::text, 'UTF8')) <= 4096),

        """;
    private const string IdentityStartSql = """CREATE TABLE "__KEYLOAD_SCHEMA__".event_identity(""";
    private const string IdentityEndSql = "PRIMARY KEY(set_name, series_id, event_id), UNIQUE(set_name, series_id, sample_sequence));";
    private const string SamplesStartSql = """CREATE TABLE "__KEYLOAD_SCHEMA__".samples(""";
    private const string SamplesEndSql = """
            PRIMARY KEY(set_name, series_id, sample_time, event_id))
        WITH (tsdb.hypertable, tsdb.partition_column='sample_time');
        CREATE INDEX samples_range ON "__KEYLOAD_SCHEMA__".samples(set_name, series_id, sample_time, sample_sequence);
        """;
    private const string InsertCountersSql = "INSERT INTO series_counter(set_name, series_id, last_sequence) SELECT $1, s.series_id, 0 FROM unnest($2::text[]) AS s(series_id)";

    internal static async Task InstallAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string schemaName, string setName, CancellationToken cancellationToken)
    {
        TimescaleSchemaLifecycle.ValidateSchemaName(schemaName);
        await using (var searchPath = new NpgsqlCommand(DdlSearchPathSql, connection, transaction))
        {
            searchPath.Parameters.AddWithValue(NpgsqlDbType.Text, schemaName + DdlSearchPathSuffix);
            await searchPath.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        }

        await ExecuteAsync(connection, transaction, InstallStage.Tables, schemaName, cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, transaction, InstallStage.Append, schemaName, cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, transaction, InstallStage.Read, schemaName, cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, transaction, InstallStage.Aggregate, schemaName, cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, transaction, InstallStage.Windows, schemaName, cancellationToken).ConfigureAwait(false);
        await using var counters = new NpgsqlCommand(InsertCountersSql, connection, transaction);
        counters.Parameters.AddWithValue(NpgsqlDbType.Text, setName);
        counters.Parameters.AddWithValue(NpgsqlDbType.Array | NpgsqlDbType.Text, CounterSeries());
        await counters.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static string BindSchema(string sql, string schemaName)
    {
        TimescaleSchemaLifecycle.ValidateSchemaName(schemaName);
        return sql.Replace(SchemaToken, schemaName, StringComparison.Ordinal);
    }

    private static string[] CounterSeries()
    {
        var names = new List<string> { TimeSeriesIntensiveProfile.SeedSeries };
        for (var repetition = 0; repetition < TimeSeriesIntensiveProfile.RepetitionCount; repetition++)
        {
            var suffix = repetition.ToString(CultureInfo.InvariantCulture);
            names.Add(TimeSeriesIntensiveProfile.WarmSeriesPrefix + suffix);
            names.Add(TimeSeriesIntensiveProfile.MeasuredSeriesPrefix + suffix);
        }

        return [.. names];
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        InstallStage stage, string schemaName, CancellationToken cancellationToken)
    {
        var sql = stage switch
        {
            InstallStage.Tables => BindSchema(CounterSql + IdentityStartSql + SampleColumnsSql +
                IdentityEndSql + SamplesStartSql + SampleColumnsSql + SamplesEndSql, schemaName),
            InstallStage.Append => TimescaleTimeSeriesIntensiveAppendSql.Create(schemaName),
            InstallStage.Read => TimescaleTimeSeriesIntensiveReadSql.Create(schemaName),
            InstallStage.Aggregate => TimescaleTimeSeriesIntensiveAggregateSql.Create(schemaName),
            InstallStage.Windows => TimescaleTimeSeriesIntensiveWindowsSql.Create(schemaName),
            _ => throw new ArgumentOutOfRangeException(nameof(stage))
        };
        await using (var setting = new NpgsqlCommand(SetDdlSql, connection, transaction))
        {
            setting.Parameters.AddWithValue(NpgsqlDbType.Text, sql);
            await setting.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        }
        await using var command = new NpgsqlCommand(ExecuteDdlSql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
