using Npgsql;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal enum TimeSeriesIntensiveFailureOrigin
{
    None,
    KeyLoad,
    PostgreSQL,
    HttpTransport,
    Client,
    Oracle,
    Unexpected
}

internal readonly record struct TimeSeriesIntensiveFailure(TimeSeriesIntensiveFailureOrigin Origin,
    ErrorCode? KeyLoadCode, int? HttpStatus, ulong? SqlState)
{
    internal static (TimeSeriesIntensiveOutcome Outcome, TimeSeriesIntensiveFailure Failure) Capture(Exception error)
    {
        try
        {
            return (Outcome(error), From(error));
        }
        catch (FormatException malformedCode)
        {
            return (error is TimeSeriesIntensiveObservedFailureException observed ? observed.Completion : Outcome(malformedCode), From(malformedCode));
        }
    }

    internal static TimeSeriesIntensiveFailure From(Exception error) => error switch
    {
        TimeSeriesIntensiveObservedFailureException { InnerException: { } original } => From(original),
        TimeSeriesIntensiveTargetCompletionException { InnerException: { } original } => From(original),
        KeyLoadTimeSeriesIntensiveProblemException native => new(TimeSeriesIntensiveFailureOrigin.KeyLoad, native.Code, native.HttpStatus, null),
        KeyLoadTimeSeriesIntensiveReplyException => new(TimeSeriesIntensiveFailureOrigin.Oracle, null, null, null),
        KeyLoadException native => new(TimeSeriesIntensiveFailureOrigin.KeyLoad, native.Code, native.StatusCode, null),
        PostgresException native => new(TimeSeriesIntensiveFailureOrigin.PostgreSQL, null, null, PackSqlState(native.SqlState)),
        NpgsqlException => new(TimeSeriesIntensiveFailureOrigin.PostgreSQL, null, null, null),
        HttpRequestException native => new(TimeSeriesIntensiveFailureOrigin.HttpTransport, null, (int?)native.StatusCode, null),
        OperationCanceledException { InnerException: PostgresException native } => From(native),
        ComparisonFailureException => new(TimeSeriesIntensiveFailureOrigin.Oracle, null, null, null),
        OperationCanceledException or TimeoutException => new(TimeSeriesIntensiveFailureOrigin.Client, null, null, null),
        _ => new(TimeSeriesIntensiveFailureOrigin.Unexpected, null, null, null)
    };

    internal static TimeSeriesIntensiveOutcome Outcome(Exception error) => error switch
    {
        TimeSeriesIntensiveObservedFailureException observed => observed.Completion,
        TimeSeriesIntensiveTargetCompletionException { InnerException: { } original } => Outcome(original),
        TimeSeriesIntensiveCallDeadlineException => TimeSeriesIntensiveOutcome.DeadlineExceeded,
        KeyLoadTimeSeriesIntensiveProblemException => TimeSeriesIntensiveOutcome.TargetFailure,
        KeyLoadTimeSeriesIntensiveReplyException => TimeSeriesIntensiveOutcome.ValidationFailure,
        KeyLoadException or PostgresException => TimeSeriesIntensiveOutcome.TargetFailure,
        NpgsqlException or HttpRequestException or TimeoutException => TimeSeriesIntensiveOutcome.TransportFailure,
        OperationCanceledException => TimeSeriesIntensiveOutcome.Cancelled,
        ComparisonFailureException => TimeSeriesIntensiveOutcome.ValidationFailure,
        _ => TimeSeriesIntensiveOutcome.UnexpectedFailure
    };

    internal static ulong PackSqlState(string sqlState)
    {
        ArgumentNullException.ThrowIfNull(sqlState);
        if (sqlState.Length != TimeSeriesIntensiveRuntimePolicy.SqlStateLength)
        {
            throw new FormatException(TimeSeriesIntensiveRuntimeErrors.InvalidSqlState);
        }

        var packed = 0UL;
        foreach (var value in sqlState)
        {
            if (!(value is >= TimeSeriesIntensiveRuntimePolicy.DigitStart and <= TimeSeriesIntensiveRuntimePolicy.DigitEnd
                or >= TimeSeriesIntensiveRuntimePolicy.UpperStart and <= TimeSeriesIntensiveRuntimePolicy.UpperEnd))
            {
                throw new FormatException(TimeSeriesIntensiveRuntimeErrors.InvalidSqlState);
            }

            packed = (packed << TimeSeriesIntensiveRuntimePolicy.AsciiBits) | value;
        }

        return packed;
    }
}
