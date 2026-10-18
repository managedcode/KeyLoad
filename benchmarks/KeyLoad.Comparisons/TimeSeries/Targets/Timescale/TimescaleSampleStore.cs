using System.Collections.Immutable;
using Npgsql;
using NpgsqlTypes;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries;

internal static class TimescaleSampleStore
{
    private const string InsertSampleSql = "INSERT INTO samples (sample_time, event_id, set_name, series_id, " +
        "sample_value, sample_sequence, tags_json) VALUES ($1, $2, $3, $4, $5, $6, $7::jsonb) ON CONFLICT DO NOTHING";
    private const string VerifySampleSql = "SELECT count(*) FILTER (WHERE sample_time=$1 AND set_name=$3 AND " +
        "series_id=$4 AND sample_value=$5 AND sample_sequence=$6 AND tags_json=$7::jsonb), count(*) " +
        "FROM samples WHERE event_id=$2";
    private const string ReadSamplesSql = "SELECT event_id, sample_time, sample_value, sample_sequence, tags_json " +
        "FROM samples WHERE set_name=$1 AND series_id=$2 AND sample_time >= $3 AND sample_time <= $4 " +
        "ORDER BY sample_time, sample_sequence";
    private const string AggregateSql = "SELECT public.time_bucket($1::interval, sample_time), SUM(sample_value) " +
        "FROM samples WHERE set_name=$2 AND series_id=$3 GROUP BY 1 ORDER BY 1";
    private const string InvalidRangeErrorCode = "BudgetExceeded";

    internal static async Task SeedAsync(string connectionString, string schemaName,
        TimeSeriesComparisonWorkload workload, CancellationToken cancellationToken)
    {
        const string TimescaleSeedFailedDetail = "TimescaleSeedFailed";

        try
        {
            await using var connection = await TimescaleSchemaLifecycle.OpenConfiguredAsync(connectionString,
                schemaName, cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            foreach (var sample in workload.Samples)
            {
                await InsertSampleAsync(connection, transaction, workload, sample, cancellationToken).ConfigureAwait(false);
                await VerifySampleAsync(connection, transaction, workload, sample, cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException error) when (TimeSeriesComparisonTargetErrors.TryGetCode(error, out _))
        {
            throw;
        }
        catch (NpgsqlException error)
        {
            throw TimeSeriesComparisonTargetErrors.Create(TimescaleSeedFailedDetail, error);
        }
        catch (TimeoutException error)
        {
            throw TimeSeriesComparisonTargetErrors.Create(TimescaleSeedFailedDetail, error);
        }
        catch (ArgumentException error)
        {
            throw TimeSeriesComparisonTargetErrors.Create(TimescaleSeedFailedDetail, error);
        }
        catch (InvalidOperationException error)
        {
            throw TimeSeriesComparisonTargetErrors.Create(TimescaleSeedFailedDetail, error);
        }
    }

    internal static async Task<TimeSeriesReadResult> ReadAsync(string connectionString, string schemaName,
        TimeSeriesComparisonWorkload workload, TimeSeriesReadRange range, CancellationToken cancellationToken)
    {
        const int FifthColumnIndex = 4;

        const int FirstColumnIndex = 0;
        const int SecondColumnIndex = 1;
        const int ThirdColumnIndex = 2;
        const int FourthColumnIndex = 3;
        const string TimescaleReadFailedToken = "TimescaleReadFailed";

        if (range.ExpectedErrorCode is not null || range.From > range.Until)
        {
            return new(false, ImmutableArray<TimeSeriesSamplePoint>.Empty,
                range.ExpectedErrorCode ?? InvalidRangeErrorCode);
        }

        try
        {
            await using var connection = await TimescaleSchemaLifecycle.OpenConfiguredAsync(connectionString,
                schemaName, cancellationToken).ConfigureAwait(false);
            await using var command = new NpgsqlCommand(ReadSamplesSql, connection);
            AddRangeParameters(command, workload, range);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var samples = ImmutableArray.CreateBuilder<TimeSeriesSamplePoint>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                samples.Add(new(reader.GetString(FirstColumnIndex), Utc(reader.GetDateTime(SecondColumnIndex)), reader.GetDouble(ThirdColumnIndex),
                    reader.GetInt64(FourthColumnIndex), reader.GetString(FifthColumnIndex)));
            }

            return new(true, samples.ToImmutable(), null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (NpgsqlException)
        {
            return new(false, ImmutableArray<TimeSeriesSamplePoint>.Empty, TimescaleReadFailedToken);
        }
        catch (TimeoutException)
        {
            return new(false, ImmutableArray<TimeSeriesSamplePoint>.Empty, TimescaleReadFailedToken);
        }
        catch (ArgumentException)
        {
            return new(false, ImmutableArray<TimeSeriesSamplePoint>.Empty, TimescaleReadFailedToken);
        }
        catch (InvalidOperationException)
        {
            return new(false, ImmutableArray<TimeSeriesSamplePoint>.Empty, TimescaleReadFailedToken);
        }
    }

    internal static async Task<ImmutableArray<TimeSeriesBucketValue>> AggregateAsync(string connectionString,
        string schemaName, TimeSeriesComparisonWorkload workload, CancellationToken cancellationToken)
    {
        const int FirstColumnIndex = 0;
        const int SecondColumnIndex = 1;
        const string TimescaleAggregateFailedDetail = "TimescaleAggregateFailed";

        try
        {
            await using var connection = await TimescaleSchemaLifecycle.OpenConfiguredAsync(connectionString,
                schemaName, cancellationToken).ConfigureAwait(false);
            await using var command = new NpgsqlCommand(AggregateSql, connection);
            command.Parameters.AddWithValue(NpgsqlDbType.Interval, workload.BucketWidth);
            command.Parameters.AddWithValue(NpgsqlDbType.Text, workload.SetName);
            command.Parameters.AddWithValue(NpgsqlDbType.Text, workload.SeriesId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var buckets = ImmutableArray.CreateBuilder<TimeSeriesBucketValue>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                buckets.Add(new(Utc(reader.GetDateTime(FirstColumnIndex)), reader.GetDouble(SecondColumnIndex)));
            }

            return buckets.ToImmutable();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException error) when (TimeSeriesComparisonTargetErrors.TryGetCode(error, out _))
        {
            throw;
        }
        catch (NpgsqlException error)
        {
            throw TimeSeriesComparisonTargetErrors.Create(TimescaleAggregateFailedDetail, error);
        }
        catch (TimeoutException error)
        {
            throw TimeSeriesComparisonTargetErrors.Create(TimescaleAggregateFailedDetail, error);
        }
        catch (ArgumentException error)
        {
            throw TimeSeriesComparisonTargetErrors.Create(TimescaleAggregateFailedDetail, error);
        }
        catch (InvalidOperationException error)
        {
            throw TimeSeriesComparisonTargetErrors.Create(TimescaleAggregateFailedDetail, error);
        }
    }

    private static async Task InsertSampleAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        TimeSeriesComparisonWorkload workload, TimeSeriesSamplePoint sample, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(InsertSampleSql, connection, transaction);
        AddSampleParameters(command, workload, sample);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task VerifySampleAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        TimeSeriesComparisonWorkload workload, TimeSeriesSamplePoint sample, CancellationToken cancellationToken)
    {
        const int FirstColumnIndex = 0;
        const int SingleItemCount = 1;
        const int SecondColumnIndex = 1;
        const string TimescaleSeedIdentityConflictDetail = "TimescaleSeedIdentityConflict";

        await using var command = new NpgsqlCommand(VerifySampleSql, connection, transaction);
        AddSampleParameters(command, workload, sample);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false) || reader.GetInt64(FirstColumnIndex) != SingleItemCount ||
            reader.GetInt64(SecondColumnIndex) != SingleItemCount)
        {
            throw TimeSeriesComparisonTargetErrors.Create(TimescaleSeedIdentityConflictDetail);
        }
    }

    private static void AddSampleParameters(NpgsqlCommand command, TimeSeriesComparisonWorkload workload,
        TimeSeriesSamplePoint sample)
    {
        command.Parameters.AddWithValue(NpgsqlDbType.TimestampTz, sample.Timestamp.ToUniversalTime());
        command.Parameters.AddWithValue(NpgsqlDbType.Text, sample.EventId);
        command.Parameters.AddWithValue(NpgsqlDbType.Text, workload.SetName);
        command.Parameters.AddWithValue(NpgsqlDbType.Text, workload.SeriesId);
        command.Parameters.AddWithValue(NpgsqlDbType.Double, sample.Value);
        command.Parameters.AddWithValue(NpgsqlDbType.Bigint, sample.Sequence);
        command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, sample.TagsJson);
    }

    private static void AddRangeParameters(NpgsqlCommand command, TimeSeriesComparisonWorkload workload,
        TimeSeriesReadRange range)
    {
        command.Parameters.AddWithValue(NpgsqlDbType.Text, workload.SetName);
        command.Parameters.AddWithValue(NpgsqlDbType.Text, workload.SeriesId);
        command.Parameters.AddWithValue(NpgsqlDbType.TimestampTz, range.From.ToUniversalTime());
        command.Parameters.AddWithValue(NpgsqlDbType.TimestampTz, range.Until.ToUniversalTime());
    }

    private static DateTimeOffset Utc(DateTime value)
        => new(DateTime.SpecifyKind(value, DateTimeKind.Utc), TimeSpan.Zero);
}
