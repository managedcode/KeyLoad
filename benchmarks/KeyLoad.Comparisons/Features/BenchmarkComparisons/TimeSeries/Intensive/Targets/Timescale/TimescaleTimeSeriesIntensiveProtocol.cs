using NpgsqlTypes;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimescaleTimeSeriesIntensiveProtocol
{
    internal const string ContextClosed = nameof(TimescaleTimeSeriesIntensiveProtocol) + ".ContextClosed";
    internal const string InvalidContext = nameof(TimescaleTimeSeriesIntensiveProtocol) + ".InvalidContext";
    internal const string InvalidInitialization = nameof(TimescaleTimeSeriesIntensiveProtocol) + ".InvalidInitialization";
    internal const string InvalidSeed = nameof(TimescaleTimeSeriesIntensiveProtocol) + ".InvalidSeed";
    internal const string InvalidReceipt = TimeSeriesIntensiveRuntimeErrors.InvalidReceipt;
    internal const string InvalidReply = nameof(TimescaleTimeSeriesIntensiveProtocol) + ".InvalidReply";
    internal const int ConnectionTimeoutSeconds = 30;
    internal const int CommandTimeoutSeconds = 30;
    internal const int CancellationTimeoutMilliseconds = 2000;
    internal const int MinimumPoolSize = 0;
    internal const int MaximumPoolSize = TimeSeriesIntensiveProfile.Concurrency;
    internal const long MinimumSequence = 1;
    internal const int SeedSequenceOffset = 1;

    internal const NpgsqlDbType SetType = NpgsqlDbType.Text;
    internal const NpgsqlDbType SeriesType = NpgsqlDbType.Text;
    internal const NpgsqlDbType CommandIdType = NpgsqlDbType.Uuid;
    internal const NpgsqlDbType EventArrayType = NpgsqlDbType.Array | NpgsqlDbType.Text;
    internal const NpgsqlDbType TimestampArrayType = NpgsqlDbType.Array | NpgsqlDbType.TimestampTz;
    internal const NpgsqlDbType ValueArrayType = NpgsqlDbType.Array | NpgsqlDbType.Double;
    internal const NpgsqlDbType TagsType = NpgsqlDbType.Jsonb;
    internal const NpgsqlDbType TimestampType = NpgsqlDbType.TimestampTz;
    internal const NpgsqlDbType ValueType = NpgsqlDbType.Double;
    internal const NpgsqlDbType IntervalType = NpgsqlDbType.Interval;
    internal const NpgsqlDbType IntegerType = NpgsqlDbType.Integer;

    internal const string SeedBatchSql = "SELECT input_ordinal, command_id, sample_sequence " +
        "FROM kld_tsi_append_batch($1, $2, $3, $4, $5, $6, $7)";
    internal const string AppendOneSql = "SELECT command_id, sample_sequence " +
        "FROM kld_tsi_append_one($1, $2, $3, $4, $5, $6, $7)";
    internal const string ReadSql = "SELECT series_id, event_id, sample_time, sample_value, sample_sequence, tags_text " +
        "FROM kld_tsi_read($1, $2, $3, $4, $5)";
    internal const string LatestSql = "SELECT series_id, event_id, sample_time, sample_value, sample_sequence, tags_text " +
        "FROM kld_tsi_latest($1, $2, $3)";
    internal const string AggregateSql = "SELECT sample_count, sample_sum, sample_min, sample_max, sample_avg " +
        "FROM kld_tsi_aggregate($1, $2, $3, $4, $5)";
    internal const string WindowsSql = "SELECT window_ordinal, window_from, window_until, sample_count, sample_sum, " +
        "sample_min, sample_max, sample_avg FROM kld_tsi_windows($1, $2, $3, $4, $5, $6, $7)";

    private static readonly string[] BatchColumnNames = ["input_ordinal", "command_id", "sample_sequence"];
    private static readonly string[] ScalarColumnNames = ["command_id", "sample_sequence"];
    private static readonly string[] SampleColumnNames = ["series_id", "event_id", "sample_time", "sample_value", "sample_sequence", "tags_text"];
    private static readonly string[] AggregateColumnNames = ["sample_count", "sample_sum", "sample_min", "sample_max", "sample_avg"];
    private static readonly string[] WindowColumnNames = ["window_ordinal", "window_from", "window_until", "sample_count", "sample_sum", "sample_min", "sample_max", "sample_avg"];

    internal static ReadOnlySpan<string> BatchColumns => BatchColumnNames;
    internal static ReadOnlySpan<string> ScalarColumns => ScalarColumnNames;
    internal static ReadOnlySpan<string> SampleColumns => SampleColumnNames;
    internal static ReadOnlySpan<string> AggregateColumns => AggregateColumnNames;
    internal static ReadOnlySpan<string> WindowColumns => WindowColumnNames;
}
