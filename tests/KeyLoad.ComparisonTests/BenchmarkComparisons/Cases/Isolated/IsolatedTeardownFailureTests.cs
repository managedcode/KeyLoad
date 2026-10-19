using System.Text.Json;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class IsolatedTeardownFailureTests
{
    private const string PrimaryMarker = "private-primary-marker";
    private const string CaptureMarker = "execution-capture-failure-marker";
    private const string CaptureTemplate = "{Marker}";
    private const string FailedStagesField = "failedStages";
    private const string CaptureDisposeStage = "capture-dispose";
    private const string LogsCloseStage = "logs-close";
    private static readonly Action<ILogger, string, Exception?> WriteCaptureMarker = LoggerMessage.Define<string>(
        LogLevel.Information, new EventId(3, nameof(PrimaryCancellationAndOwnedCaptureFailurePreserveBothOriginalObjects)), CaptureTemplate);

    [Test]
    public async Task PrimaryCancellationAndCopyFailureRetainOriginalObjectsAndSafeReceipt()
    {
        var directory = Directory.CreateTempSubdirectory("keyload-execution-teardown-failure-");
        var evidence = Path.Combine(directory.FullName, "evidence");
        var output = Path.Combine(directory.FullName, "output");
        var dataRoot = Path.Combine(directory.FullName, "absent-data");
        IsolatedTeardownSupport.CreateRawCopyConflict(output, evidence);
        try
        {
            await using var fixture = await IsolatedTeardownSupport.CreateFixtureAsync(
                Path.Combine(evidence, "progress.json"));
            using var owner = new CancellationTokenSource();
            var primary = await IsolatedTeardownSupport.CancelRunnerWaitAsync(fixture, owner);
            var primaryToken = primary.CancellationToken;
            var failure = await CaptureAsync(() => IsolatedTeardownSupport.CompleteAsync(
                fixture, output, evidence, dataRoot, primary));
            await Assert.That(failure.InnerExceptions.Count >= 2).IsTrue();
            await Assert.That(ReferenceEquals(primary, failure.InnerExceptions[0])).IsTrue();
            await Assert.That(primaryToken.IsCancellationRequested).IsTrue();
            await Assert.That(((OperationCanceledException)failure.InnerExceptions[0]).CancellationToken)
                .IsEqualTo(primaryToken);
            await Assert.That(owner.IsCancellationRequested).IsTrue();
            await Assert.That(failure.InnerExceptions.Skip(1).Any(item => item is IOException)).IsTrue();
            var receipt = await File.ReadAllTextAsync(Path.Combine(evidence, IsolatedTeardownSupport.ReceiptFile));
            await Assert.That(receipt).Contains("\"primaryFailure\":true");
            await Assert.That(receipt).Contains("\"raw\"");
            await Assert.That(receipt).DoesNotContain(PrimaryMarker);
            await Assert.That(fixture.Capture.StopAsync().IsCompleted).IsTrue();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Test]
    public async Task PrimaryCancellationAndOwnedCaptureFailurePreserveBothOriginalObjects()
    {
        using var directory = new ArtifactTestDirectory();
        var evidence = Path.Combine(directory.Root, "evidence");
        var output = Path.Combine(directory.Root, "output");
        Directory.CreateDirectory(evidence);
        await using var fixture = await IsolatedTeardownSupport.CreateFixtureAsync(
            Path.Combine(directory.Root, "fixture-progress.json"));
        var work = new IsolatedOwnedWork(ExecutionPolicyFixture.Harness());
        var original = Task.CompletedTask;
        Exception? bodyFailure = null;
        try
        {
            var captureFailure = new InvalidOperationException(CaptureMarker);
            var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            work.BeginCapture(fixture.Application, [fixture.Node.Name], Path.Combine(evidence, "progress.json"),
                line => ObserveCaptureFailure(line, observed, captureFailure));
            await PublishCaptureFailureAsync(fixture, observed);
            using var cancellation = new CancellationTokenSource();
            var primary = await IsolatedTeardownSupport.CancelRunnerWaitAsync(fixture, cancellation);
            original = IsolatedTeardown.CompleteAsync(fixture.Application,
                output, evidence, Path.Combine(directory.Root, "absent-data"), [fixture.Runner, fixture.Node], work, primary);
            var failure = await CaptureAsync(() => original);
            await Assert.That(ReferenceEquals(failure.InnerExceptions[0], primary)).IsTrue();
            await Assert.That(failure.InnerExceptions.Count(error => ReferenceEquals(error, captureFailure))).IsEqualTo(1);
            await Assert.That(work.Capture!.StopAsync().IsCompleted).IsTrue();
            using var receipt = JsonDocument.Parse(await File.ReadAllTextAsync(
                Path.Combine(evidence, IsolatedTeardownSupport.ReceiptFile)));
            await Assert.That(receipt.RootElement.GetProperty(FailedStagesField).EnumerateArray()
                .Count(stage => stage.GetString() == CaptureDisposeStage)).IsEqualTo(1);
        }
        catch (Exception failure)
        {
            bodyFailure = failure;
            throw;
        }
        finally
        {
            await IsolatedTeardown.SettleOwnerDisposalAsync(
                bodyFailure is null ? original : Task.FromException(bodyFailure), work.DisposeAsync().AsTask());
        }
    }

    private static void ObserveCaptureFailure(string line, TaskCompletionSource observed, Exception failure)
    {
        if (line.Contains(CaptureMarker, StringComparison.Ordinal))
        {
            observed.TrySetResult();
            throw failure;
        }
    }

    private static async Task PublishCaptureFailureAsync(ComparisonProgressFixture fixture, TaskCompletionSource observed)
    {
        var notifications = fixture.Application.Services.GetRequiredService<ResourceNotificationService>();
        await notifications.PublishUpdateAsync(fixture.Runner, snapshot => snapshot with { State = KnownResourceStates.Running });
        var logger = fixture.Application.Services.GetRequiredService<ResourceLoggerService>().GetLogger(fixture.Runner);
        WriteCaptureMarker(logger, CaptureMarker, null);
        using var deadline = new CancellationTokenSource(
            ExecutionPolicyFixture.Harness().Value.ConcurrentCaptureDisposalTimeout, TimeProvider.System);
        await observed.Task.WaitAsync(deadline.Token);
    }

    [Test]
    public async Task DuplicateTaskObservationPreservesDistinctStagesAndOriginalFailureOrder()
    {
        var primary = new OperationCanceledException(PrimaryMarker);
        var original = new IOException(CaptureMarker);
        var independent = new IOException(CaptureMarker);
        var failures = new IsolatedTeardownFailures(primary);
        failures.Record(CaptureDisposeStage, original);
        failures.Record(CaptureDisposeStage, original);
        failures.Record(CaptureDisposeStage, independent);
        failures.Record(LogsCloseStage, original);
        var aggregate = await CaptureAsync(() =>
        {
            failures.ThrowIfAny();
            return Task.CompletedTask;
        });
        await Assert.That(aggregate.InnerExceptions.Count).IsEqualTo(3);
        await Assert.That(ReferenceEquals(aggregate.InnerExceptions[0], primary)).IsTrue();
        await Assert.That(ReferenceEquals(aggregate.InnerExceptions[1], original)).IsTrue();
        await Assert.That(ReferenceEquals(aggregate.InnerExceptions[2], independent)).IsTrue();
        using var receipt = JsonDocument.Parse(failures.CreateReceipt());
        await Assert.That(receipt.RootElement.GetProperty(FailedStagesField).EnumerateArray()
            .Select(stage => stage.GetString()).SequenceEqual([CaptureDisposeStage, CaptureDisposeStage, LogsCloseStage])).IsTrue();
    }

    private static async Task<AggregateException> CaptureAsync(Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch (AggregateException failure)
        {
            return failure;
        }
        throw new InvalidOperationException("Execution teardown did not propagate its primary and cleanup failures.");
    }
}
