using NpgsqlTypes;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimescaleTimeSeriesIntensiveProtocol
{
    private const string BatchColumnNamesSampleSequenceToken = "sample_sequence";
    private const string SampleTimeToken = "sample_time";
    private const string SampleValueToken = "sample_value";
    private const string SampleColumnNamesResultText = "sample_sequence";
    private const string SampleColumnNamesSampleColumnNamesResultText = "tags_text";
    private const string SampleMinToken = "sample_min";
    private const string SampleMaxToken = "sample_max";
    private const string AggregateColumnNamesResultText = "sample_avg";
    private const string WindowUntilToken = "window_until";
    private const string WindowColumnNamesSampleCountToken = "sample_count";
    private const string WindowColumnNamesResultText = "sample_sum";
    private const string WindowColumnNamesWindowColumnNamesResultText = "sample_min";

    private const string InputOrdinalToken = "input_ordinal";
    private const string CommandIdToken = "command_id";
    private const string SampleSequenceToken = "sample_sequence";
    private const string SeriesIdToken = "series_id";
    private const string EventIdToken = "event_id";
    private const string SampleCountToken = "sample_count";
    private const string SampleSumToken = "sample_sum";
    private const string WindowOrdinalToken = "window_ordinal";
    private const string WindowFromToken = "window_from";

    internal const string ContextClosed = nameof(TimescaleTimeSeriesIntensiveProtocol) + ".ContextClosed";
    internal const string InvalidContext = nameof(TimescaleTimeSeriesIntensiveProtocol) + ".InvalidContext";
    internal const string InvalidInitialization = nameof(TimescaleTimeSeriesIntensiveProtocol) + ".InvalidInitialization";
    internal const string InvalidSeed = nameof(TimescaleTimeSeriesIntensiveProtocol) + ".InvalidSeed";
    internal const string InvalidReceipt = TimeSeriesIntensiveRuntimeErrors.InvalidReceipt;
    internal const string InvalidReply = nameof(TimescaleTimeSeriesIntensiveProtocol) + ".InvalidReply";
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

    private static readonly string[] BatchColumnNames = [InputOrdinalToken, CommandIdToken, BatchColumnNamesSampleSequenceToken];
    private static readonly string[] ScalarColumnNames = [CommandIdToken, SampleSequenceToken];
    private static readonly string[] SampleColumnNames = [SeriesIdToken, EventIdToken, SampleTimeToken, SampleValueToken, SampleColumnNamesResultText, SampleColumnNamesSampleColumnNamesResultText];
    private static readonly string[] AggregateColumnNames = [SampleCountToken, SampleSumToken, SampleMinToken, SampleMaxToken, AggregateColumnNamesResultText];
    private static readonly string[] WindowColumnNames = [WindowOrdinalToken, WindowFromToken, WindowUntilToken, WindowColumnNamesSampleCountToken, WindowColumnNamesResultText, WindowColumnNamesWindowColumnNamesResultText, SampleMaxToken, AggregateColumnNamesResultText];

    internal static ReadOnlySpan<string> BatchColumns => BatchColumnNames;
    internal static ReadOnlySpan<string> ScalarColumns => ScalarColumnNames;
    internal static ReadOnlySpan<string> SampleColumns => SampleColumnNames;
    internal static ReadOnlySpan<string> AggregateColumns => AggregateColumnNames;
    internal static ReadOnlySpan<string> WindowColumns => WindowColumnNames;
}
