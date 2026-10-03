using System.Runtime.ExceptionServices;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class IsolatedKurrentVolumeRegressionFailureTests
{
    [Test]
    public async Task CleanupStagesContinueAndFirstNonfatalFailureIsRethrown()
    {
        var failures = new IsolatedKurrentVolumeRegressionFailureCollector();
        var first = new InvalidOperationException("first cleanup stage");
        var stages = 0;
        await failures.AttemptCleanupAsync(() => FailAfterStageAsync(first, () => stages++));
        await failures.AttemptCleanupAsync(() => CompleteStage(() => stages++));
        await Assert.That(stages).IsEqualTo(2);

        InvalidOperationException? thrown = null;
        try
        {
            failures.ThrowAfterCleanup(null);
        }
        catch (InvalidOperationException error)
        {
            thrown = error;
        }
        await Assert.That(ReferenceEquals(thrown, first)).IsTrue();
    }

    [Test]
    public async Task OriginalNonfatalPrimaryPrecedesFirstNonfatalCleanupFailure()
    {
        var failures = new IsolatedKurrentVolumeRegressionFailureCollector();
        var cleanup = new IOException("cleanup failure");
        var primary = new InvalidOperationException("primary failure");
        await failures.AttemptCleanupAsync(() => Task.FromException(cleanup));

        InvalidOperationException? thrown = null;
        try
        {
            failures.ThrowAfterCleanup(ExceptionDispatchInfo.Capture(primary));
        }
        catch (InvalidOperationException error)
        {
            thrown = error;
        }
        await Assert.That(ReferenceEquals(thrown, primary)).IsTrue();
    }

    [Test]
    public async Task NestedRuntimeFatalCleanupFailureOverridesPrimaryAndLaterStagesRun()
    {
        var fatal = RequireRuntimeRejectedArraySize();
        var nested = new InvalidOperationException("outer", new AggregateException(
            new IOException("other branch"), new ArgumentException("inner", fatal)));
        var failures = new IsolatedKurrentVolumeRegressionFailureCollector();
        var laterStageRan = false;
        await failures.AttemptCleanupAsync(() => Task.FromException(nested));
        await failures.AttemptCleanupAsync(() => CompleteStage(() => laterStageRan = true));

        OutOfMemoryException? thrown = null;
        try
        {
            failures.ThrowAfterCleanup(ExceptionDispatchInfo.Capture(new InvalidOperationException("primary")));
        }
        catch (OutOfMemoryException error)
        {
            thrown = error;
        }
        await Assert.That(laterStageRan).IsTrue();
        await Assert.That(ReferenceEquals(thrown, fatal)).IsTrue();
    }

    [Test]
    public async Task OriginalPrimaryFatalPrecedesFatalCleanupFailure()
    {
        var primaryFatal = RequireRuntimeRejectedArraySize();
        var cleanupFatal = RequireRuntimeRejectedArraySize();
        var primary = new InvalidOperationException("primary wrapper", primaryFatal);
        var cleanup = new AggregateException(new IOException("other branch"),
            new InvalidOperationException("cleanup wrapper", cleanupFatal));
        var failures = new IsolatedKurrentVolumeRegressionFailureCollector();
        await failures.AttemptCleanupAsync(() => Task.FromException(cleanup));

        OutOfMemoryException? thrown = null;
        try
        {
            failures.ThrowAfterCleanup(ExceptionDispatchInfo.Capture(primary));
        }
        catch (OutOfMemoryException error)
        {
            thrown = error;
        }
        await Assert.That(ReferenceEquals(thrown, primaryFatal)).IsTrue();
    }

    private static async Task FailAfterStageAsync(Exception error, Action stage)
    {
        stage();
        await Task.Yield();
        throw error;
    }

    private static Task CompleteStage(Action stage)
    {
        stage();
        return Task.CompletedTask;
    }

    private static OutOfMemoryException RequireRuntimeRejectedArraySize()
    {
        try
        {
            _ = Array.CreateInstance(typeof(byte), int.MaxValue, int.MaxValue);
        }
        catch (OutOfMemoryException error)
        {
            return error;
        }
        throw new InvalidOperationException("Runtime did not reject the impossible array dimensions.");
    }
}
