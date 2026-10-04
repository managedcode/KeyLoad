using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>AC-ROUTE-003/REP-004: actual stage faults remain observable without substituting node shutdown work.</summary>
internal sealed class ServerFailureObserverTests
{
    private const string FilePrefix = "keyload-failure-observer-";
    private const string FileSuffix = ".tmp";
    private const string GuidFormat = "N";
    private const string FileContents = "actual filesystem stage";
    private const string MissingOriginatingTask = "The task cancellation had no originating task.";
    private const int SingleFailure = 1;
    private const int MultipleFailures = 2;

    /// <summary>A successful real asynchronous file write completes and does not add a failure.</summary>
    [Test]
    public async Task SuccessfulFilesystemStageCompletesWithoutFailure()
    {
        var path = NewPath();
        try
        {
            var failures = new List<Exception>();
            await ServerFailureObserver.ObserveAsync(() => File.WriteAllTextAsync(path, FileContents,
                TestContext.Current!.Execution.CancellationToken), failures);
            await Assert.That(failures).IsEmpty();
            await Assert.That(await File.ReadAllTextAsync(path, TestContext.Current!.Execution.CancellationToken)).IsEqualTo(FileContents);
        }
        finally { File.Delete(path); }
    }

    /// <summary>A synchronous missing-file invocation is captured with its original exception identity.</summary>
    [Test]
    public async Task SynchronousFilesystemFailureRetainsOriginalException()
    {
        var path = NewPath();
        FileNotFoundException? original = null;
        Task Stage()
        {
            try
            {
                using var input = File.OpenRead(path);
                return Task.CompletedTask;
            }
            catch (FileNotFoundException error)
            {
                original = error;
                throw;
            }
        }
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(Stage, failures);
        await Assert.That(failures.Count).IsEqualTo(SingleFailure);
        await Assert.That(original).IsNotNull();
        await Assert.That(failures[0]).IsSameReferenceAs(original);
    }

    /// <summary>A genuine asynchronous missing-file fault is retained from the actual stage task.</summary>
    [Test]
    public async Task AsynchronousFilesystemFailureRetainsActualTaskException()
    {
        var stage = ReadMissingAsync(NewPath());
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => stage, failures);
        await Assert.That(stage.IsFaulted).IsTrue();
        await Assert.That(failures.Count).IsEqualTo(SingleFailure);
        await Assert.That(failures[0].GetType()).IsEqualTo(typeof(FileNotFoundException));
        await Assert.That(failures[0]).IsSameReferenceAs(stage.Exception!.InnerExceptions[0]);
    }

    /// <summary>An actual cancelled runtime task is recorded with its task and cancellation token.</summary>
    [Test]
    public async Task CancelledStageRetainsActualTaskCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var stage = Task.Delay(Timeout.InfiniteTimeSpan, TimeProvider.System, cancellation.Token);
        await cancellation.CancelAsync();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => stage, failures);
        await Assert.That(stage.IsCanceled).IsTrue();
        await Assert.That(failures.Count).IsEqualTo(SingleFailure);
        await Assert.That(failures[0].GetType()).IsEqualTo(typeof(TaskCanceledException));
        var failure = (TaskCanceledException)failures[0];
        await Assert.That(failure.Task).IsNotNull();
        var canceledTask = failure.Task ?? throw new InvalidOperationException(MissingOriginatingTask);
        await Assert.That(canceledTask).IsSameReferenceAs(stage);
        await Assert.That(failure.CancellationToken).IsEqualTo(cancellation.Token);
    }

    /// <summary>Multiple real filesystem failures retain their identities and order without replacing a previously collected failure.</summary>
    [Test]
    public async Task AllActualInnerExceptionsAreAppendedAfterPreexistingFailure()
    {
        var previous = ReadMissingAsync(NewPath());
        await previous.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        var retained = previous.Exception!.InnerExceptions[0];
        var failures = new List<Exception> { retained };
        var stages = Enumerable.Range(0, MultipleFailures).Select(_ => ReadMissingAsync(NewPath())).ToArray();
        var stage = Task.WhenAll(stages);
        await ServerFailureObserver.ObserveAsync(() => stage, failures);
        var actual = stage.Exception!.InnerExceptions;
        await Assert.That(actual.Count).IsEqualTo(MultipleFailures);
        await Assert.That(failures.Count).IsEqualTo(actual.Count + SingleFailure);
        await Assert.That(failures[0]).IsSameReferenceAs(retained);
        for (var index = 0; index < actual.Count; index++)
        {
            await Assert.That(failures[index + SingleFailure]).IsSameReferenceAs(actual[index]);
        }
    }

    private static string NewPath() => Path.Combine(Path.GetTempPath(), FilePrefix + Guid.NewGuid().ToString(GuidFormat) + FileSuffix);

    /// <summary>A real synchronous file cleanup executes inline and an empty failure list emits no exception.</summary>
    [Test]
    public async Task SuccessfulSynchronousCleanupLeavesNoFailureToThrow()
    {
        var path = NewPath();
        try
        {
            await File.WriteAllTextAsync(path, FileContents, TestContext.Current!.Execution.CancellationToken);
            var failures = new List<Exception>();
            ServerFailureObserver.Observe(() => File.Delete(path), failures);
            ServerFailureObserver.ThrowIfAny(failures);
            await Assert.That(File.Exists(path)).IsFalse();
            await Assert.That(failures).IsEmpty();
        }
        finally { File.Delete(path); }
    }

    /// <summary>A synchronous filesystem failure is observed and emitted as the same original exception with its existing stack trace.</summary>
    [Test]
    public async Task SynchronousFailureIsRethrownWithOriginalIdentityAndTrace()
    {
        var path = NewPath();
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() => { using var input = File.OpenRead(path); }, failures);
        await Assert.That(failures.Count).IsEqualTo(SingleFailure);
        var original = failures[0];
        var trace = original.StackTrace;
        await Assert.That(trace).IsNotNull();
        var emitted = Assert.ThrowsExactly<FileNotFoundException>(() => ServerFailureObserver.ThrowIfAny(failures));
        await Assert.That(emitted).IsSameReferenceAs(original);
        await Assert.That(emitted.FileName).IsEqualTo(path);
        await Assert.That(emitted.StackTrace!.StartsWith(trace!, StringComparison.Ordinal)).IsTrue();
    }

    /// <summary>Aggregate emission retains every actual filesystem error in collected order without changing their identities.</summary>
    [Test]
    public async Task MultipleFilesystemFailuresAreEmittedInOriginalOrder()
    {
        var failures = new List<Exception>();
        var actual = Task.WhenAll(Enumerable.Range(0, MultipleFailures).Select(_ => ReadMissingAsync(NewPath())));
        await ServerFailureObserver.ObserveAsync(() => actual, failures);
        var emitted = Assert.ThrowsExactly<AggregateException>(() => ServerFailureObserver.ThrowIfAny(failures));
        await Assert.That(emitted.InnerExceptions.Count).IsEqualTo(MultipleFailures);
        for (var index = 0; index < emitted.InnerExceptions.Count; index++)
        {
            await Assert.That(emitted.InnerExceptions[index]).IsSameReferenceAs(failures[index]);
            await Assert.That(emitted.InnerExceptions[index]).IsSameReferenceAs(actual.Exception!.InnerExceptions[index]);
        }
    }

    private static async Task ReadMissingAsync(string path)
    {
        await File.ReadAllTextAsync(path, TestContext.Current!.Execution.CancellationToken);
    }
}
