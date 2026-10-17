using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;
using Npgsql;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class TimeSeriesIntensiveFailureMappingTests
{
    [Test]
    public async Task AcTsi004NativeCodesAndStatusesRemainActualExceptionValues()
    {
        var keyLoad = new KeyLoadException(ErrorCode.UnknownWriteOutcome, "safe fixture detail", 503);
        await Assert.That(TimeSeriesIntensiveFailure.From(keyLoad)).IsEqualTo(
            new TimeSeriesIntensiveFailure(TimeSeriesIntensiveFailureOrigin.KeyLoad, ErrorCode.UnknownWriteOutcome, 503, null));
        var postgres = new PostgresException("native fixture detail", "ERROR", "ERROR", "23505");
        await Assert.That(TimeSeriesIntensiveFailure.From(postgres)).IsEqualTo(
            new TimeSeriesIntensiveFailure(TimeSeriesIntensiveFailureOrigin.PostgreSQL, null, null, 0x3233353035));
        await Assert.That(TimeSeriesIntensiveFailure.Outcome(postgres)).IsEqualTo(TimeSeriesIntensiveOutcome.TargetFailure);
        var http = new HttpRequestException("transport fixture detail", null, System.Net.HttpStatusCode.BadGateway);
        await Assert.That(TimeSeriesIntensiveFailure.From(http).HttpStatus).IsEqualTo(502);
        await Assert.That(TimeSeriesIntensiveFailure.Outcome(http)).IsEqualTo(TimeSeriesIntensiveOutcome.TransportFailure);
    }

    [Test]
    public async Task AcTsi004UnknownAndValidationFailuresCannotAcquireInventedNativeCodes()
    {
        var unknown = TimeSeriesIntensiveFailure.From(new InvalidOperationException("unknown fixture detail"));
        await Assert.That(unknown).IsEqualTo(new TimeSeriesIntensiveFailure(TimeSeriesIntensiveFailureOrigin.Unexpected, null, null, null));
        await Assert.That(TimeSeriesIntensiveFailure.From(new ComparisonFailureException("oracle fixture detail")).Origin)
            .IsEqualTo(TimeSeriesIntensiveFailureOrigin.Oracle);
        await Assert.That(TimeSeriesIntensiveFailure.Outcome(new OperationCanceledException())).IsEqualTo(TimeSeriesIntensiveOutcome.Cancelled);
        await Assert.That(TimeSeriesIntensiveFailure.Outcome(new TimeSeriesIntensiveCallDeadlineException()))
            .IsEqualTo(TimeSeriesIntensiveOutcome.DeadlineExceeded);
    }
}
