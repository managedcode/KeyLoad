namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class KeyLoadTimeSeriesIntensiveProblemException : InvalidOperationException
{
    public KeyLoadTimeSeriesIntensiveProblemException()
        : this((ErrorCode?)null, (int?)null)
    {
    }

    public KeyLoadTimeSeriesIntensiveProblemException(string message)
        : base(KeyLoadTimeSeriesIntensiveProtocol.InvalidResult)
    {
        ArgumentNullException.ThrowIfNull(message);
    }

    public KeyLoadTimeSeriesIntensiveProblemException(string message, Exception innerException)
        : base(KeyLoadTimeSeriesIntensiveProtocol.InvalidResult)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(innerException);
    }

    internal KeyLoadTimeSeriesIntensiveProblemException(ErrorCode? code, int? httpStatus)
        : base(KeyLoadTimeSeriesIntensiveProtocol.InvalidResult)
    {
        Code = code;
        HttpStatus = httpStatus;
    }

    internal ErrorCode? Code { get; }
    internal int? HttpStatus { get; }
}
