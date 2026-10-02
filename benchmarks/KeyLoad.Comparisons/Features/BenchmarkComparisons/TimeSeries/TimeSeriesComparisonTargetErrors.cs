namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries;

internal static class TimeSeriesComparisonTargetErrors
{
    private const string ErrorCodeDataKey = "KeyLoad.TimeSeriesComparison.ErrorCode";

    internal static InvalidOperationException Create(string errorCode, Exception? innerException = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        var error = new InvalidOperationException("A time-series comparison target operation failed.", innerException);
        error.Data[ErrorCodeDataKey] = errorCode;
        return error;
    }

    internal static bool TryGetCode(InvalidOperationException error, out string errorCode)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (error.Data[ErrorCodeDataKey] is string code)
        {
            errorCode = code;
            return true;
        }

        errorCode = string.Empty;
        return false;
    }
}
