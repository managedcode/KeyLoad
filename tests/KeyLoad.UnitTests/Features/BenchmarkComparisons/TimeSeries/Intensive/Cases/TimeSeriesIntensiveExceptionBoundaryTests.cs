using KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class TimeSeriesIntensiveExceptionBoundaryTests
{
    [Test]
    public async Task AcTsi004OrdinaryExceptionInputsRemainReportable()
    {
        foreach (var error in new Exception[] { new InvalidOperationException(), new IOException(),
            new OperationCanceledException(), new TimeoutException(), new AggregateException(new ArgumentException()) })
        {
            await Assert.That(TimeSeriesIntensiveExceptionBoundary.IsNonfatal(error)).IsTrue();
        }
    }

    [Test]
    public async Task AcTsi004CatastrophicExceptionInputsAndWrappedCausesPropagate()
    {
        foreach (var error in new Exception[] { FatalInput(typeof(OutOfMemoryException)), FatalInput(typeof(StackOverflowException)),
            FatalInput(typeof(AccessViolationException)), new AggregateException(new InvalidOperationException(), FatalInput(typeof(OutOfMemoryException))),
            new InvalidOperationException("wrapped fixture", FatalInput(typeof(AccessViolationException))) })
        {
            await Assert.That(TimeSeriesIntensiveExceptionBoundary.IsNonfatal(error)).IsFalse();
        }
    }

    private static Exception FatalInput(Type type)
    {
        if (type != typeof(OutOfMemoryException) && type != typeof(StackOverflowException) && type != typeof(AccessViolationException))
        {
            throw new ArgumentOutOfRangeException(nameof(type));
        }

        // These real BCL objects are classification inputs; no reserved exception is raised.
        return (Exception)(Activator.CreateInstance(type) ?? throw new InvalidOperationException("Missing BCL exception constructor."));
    }
}
