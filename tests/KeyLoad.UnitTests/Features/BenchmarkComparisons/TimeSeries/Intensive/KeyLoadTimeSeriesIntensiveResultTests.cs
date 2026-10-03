using KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;
using ManagedCode.Communication;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class KeyLoadTimeSeriesIntensiveResultTests
{
    [Test]
    public async Task AcTsi006ActualProblemNamesAndReturnedStatusesArePreserved()
    {
        var cases = new[]
        {
            (ErrorCode.Conflict, 409),
            (ErrorCode.BudgetExceeded, 429),
            (ErrorCode.Cancelled, 400),
            (ErrorCode.UnknownWriteOutcome, 503),
            (ErrorCode.Conflict, 418)
        };

        foreach (var (code, status) in cases)
        {
            var result = Failed<SampleAggregate>(Problem(code.ToString(), status));
            var error = Assert.ThrowsExactly<KeyLoadTimeSeriesIntensiveProblemException>(() =>
                KeyLoadTimeSeriesIntensiveResult.Value(result));
            await Assert.That(error.Code).IsEqualTo(code);
            await Assert.That(error.HttpStatus).IsEqualTo(status);
        }
    }

    [Test]
    public async Task AcTsi006UnknownProblemNamesAndUnavailableStatusStayUnknown()
    {
        foreach (var raw in new string?[] { "future-code", "conflict", "2", "", null })
        {
            var error = Assert.ThrowsExactly<KeyLoadTimeSeriesIntensiveProblemException>(() =>
                KeyLoadTimeSeriesIntensiveResult.Value(Failed<SampleAggregate>(Problem(raw, 0))));
            await Assert.That(error.Code).IsNull();
            await Assert.That(error.HttpStatus).IsNull();
        }

        var missing = Assert.ThrowsExactly<KeyLoadTimeSeriesIntensiveProblemException>(() =>
            KeyLoadTimeSeriesIntensiveResult.Value(default(Result<SampleAggregate>)));
        await Assert.That(missing.Code).IsNull();
        await Assert.That(missing.HttpStatus).IsNull();

        var unknownWithStatus = Assert.ThrowsExactly<KeyLoadTimeSeriesIntensiveProblemException>(() =>
            KeyLoadTimeSeriesIntensiveResult.Value(Failed<SampleAggregate>(Problem("future-code", 418))));
        await Assert.That(unknownWithStatus.Code).IsNull();
        await Assert.That(unknownWithStatus.HttpStatus).IsEqualTo(418);
    }

    [Test]
    public async Task AcTsi005DefaultSuccessWrappersFailButRealEmptyResultsRemainValid()
    {
        Assert.ThrowsExactly<KeyLoadTimeSeriesIntensiveReplyException>(() =>
            KeyLoadTimeSeriesIntensiveResult.Value(Result<SampleAggregateWindowsResult>.Succeed(
                (SampleAggregateWindowsResult)null!)));
        Assert.ThrowsExactly<KeyLoadTimeSeriesIntensiveReplyException>(() =>
            KeyLoadTimeSeriesIntensiveResult.Windows(Result<SampleAggregateWindowsResult>.Succeed(
                new SampleAggregateWindowsResult(default))));
        Assert.ThrowsExactly<KeyLoadTimeSeriesIntensiveReplyException>(() =>
            KeyLoadTimeSeriesIntensiveResult.Raw(Result<SampleRecord[]>.Succeed((SampleRecord[])null!)));

        var raw = KeyLoadTimeSeriesIntensiveResult.Raw(Result<SampleRecord[]>.Succeed(Array.Empty<SampleRecord>()));
        var windows = KeyLoadTimeSeriesIntensiveResult.Windows(
            Result<SampleAggregateWindowsResult>.Succeed(new SampleAggregateWindowsResult([])));
        var latest = KeyLoadTimeSeriesIntensiveResult.Latest(
            Result<LatestSampleResult>.Succeed(new LatestSampleResult(null)));
        await Assert.That(raw.IsEmpty).IsTrue();
        await Assert.That(windows.IsEmpty).IsTrue();
        await Assert.That(latest).IsNull();
    }

    [Test]
    public async Task AcTsi006CompactExceptionsUseFixedMessagesAndNoInferredFacts()
    {
        var problem = new KeyLoadTimeSeriesIntensiveProblemException();
        var reply = new KeyLoadTimeSeriesIntensiveReplyException();
        await Assert.That(problem.Message).IsEqualTo(KeyLoadTimeSeriesIntensiveProtocol.InvalidResult);
        await Assert.That(problem.Code).IsNull();
        await Assert.That(problem.HttpStatus).IsNull();
        await Assert.That(reply.Message).IsEqualTo(KeyLoadTimeSeriesIntensiveProtocol.InvalidReceipt);
        await Assert.That(reply.ObservedSequence).IsNull();
    }

    private static Result<T> Failed<T>(Problem problem) => problem;

    private static Problem Problem(string? code, int status) => new()
    {
        ErrorCode = code!,
        StatusCode = status,
        Title = "untrusted detail",
        Detail = "untrusted detail"
    };
}
