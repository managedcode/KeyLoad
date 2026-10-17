using System.Text.Json;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.UnitTests.Features.BenchmarkComparisons;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class IsolatedNativeTeardownFailureTests
{
    private const string PrimaryMarker = "private-primary-marker";
    private const string CaptureMarker = "native-capture-failure-marker";
    private const string CaptureTemplate = "{Marker}";
    private const string FailedStagesField = "failedStages";
    private const string CaptureDisposeStage = "capture-dispose";
    private const string LogsCloseStage = "logs-close";
    private static readonly Action<ILogger, string, Exception?> WriteCaptureMarker = LoggerMessage.Define<string>(
        LogLevel.Information, new EventId(3, nameof(PrimaryCancellationAndOwnedCaptureFailurePreserveBothOriginalObjects)), CaptureTemplate);

    [Test]
    public async Task PrimaryCancellationAndNativeCopyFailureRetainOriginalObjectsAndSafeReceipt()
    {
        var directory = Directory.CreateTempSubdirectory("keyload-native-teardown-failure-");
        var evidence = Path.Combine(directory.FullName, "evidence");
        var output = Path.Combine(directory.FullName, "output");
        var dataRoot = Path.Combine(directory.FullName, "absent-data");
        IsolatedNativeTeardownNativeSupport.CreateRawCopyConflict(output, evidence);
        try
        {
            await using var fixture = await IsolatedNativeTeardownNativeSupport.CreateFixtureAsync(
                Path.Combine(evidence, "progress.json"));
            using var owner = new CancellationTokenSource();
            var primary = await IsolatedNativeTeardownNativeSupport.CancelNativeRunnerWaitAsync(fixture, owner);
            var primaryToken = primary.CancellationToken;
            var failure = await CaptureAsync(() => IsolatedNativeTeardownNativeSupport.CompleteAsync(
                fixture, output, evidence, dataRoot, primary));
            await Assert.That(failure.InnerExceptions.Count >= 2).IsTrue();
            await Assert.That(ReferenceEquals(primary, failure.InnerExceptions[0])).IsTrue();
            await Assert.That(primaryToken.IsCancellationRequested).IsTrue();
            await Assert.That(((OperationCanceledException)failure.InnerExceptions[0]).CancellationToken)
                .IsEqualTo(primaryToken);
            await Assert.That(owner.IsCancellationRequested).IsTrue();
            await Assert.That(failure.InnerExceptions.Skip(1).Any(item => item is IOException)).IsTrue();
            var receipt = await File.ReadAllTextAsync(Path.Combine(evidence, IsolatedNativeTeardownNativeSupport.ReceiptFile));
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
        using var directory = new ImageBundleTestDirectory();
        var evidence = Path.Combine(directory.Root, "evidence");
        var output = Path.Combine(directory.Root, "output");
        Directory.CreateDirectory(evidence);
        await using var fixture = await IsolatedNativeTeardownNativeSupport.CreateFixtureAsync(
            Path.Combine(directory.Root, "fixture-progress.json"));
        var work = new IsolatedNativeOwnedWork(NativeExecutionPolicyFixture.Harness());
        var captureFailure = new InvalidOperationException(CaptureMarker);
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        work.BeginCapture(fixture.Application, [fixture.Node.Name], Path.Combine(evidence, "progress.json"), line =>
        {
            if (line.Contains(CaptureMarker, StringComparison.Ordinal))
            {
                observed.TrySetResult();
                throw captureFailure;
            }
        });
        await PublishCaptureFailureAsync(fixture, observed);
        using var cancellation = new CancellationTokenSource();
        var primary = await IsolatedNativeTeardownNativeSupport.CancelNativeRunnerWaitAsync(fixture, cancellation);
        var failure = await CaptureAsync(() => IsolatedNativeTeardown.CompleteAsync(fixture.Application,
            output, evidence, Path.Combine(directory.Root, "absent-data"), [fixture.Runner, fixture.Node], work, primary));
        await Assert.That(ReferenceEquals(failure.InnerExceptions[0], primary)).IsTrue();
        await Assert.That(failure.InnerExceptions.Count(error => ReferenceEquals(error, captureFailure))).IsEqualTo(1);
        await Assert.That(work.Capture!.StopAsync().IsCompleted).IsTrue();
        using var receipt = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(evidence, IsolatedNativeTeardownNativeSupport.ReceiptFile)));
        await Assert.That(receipt.RootElement.GetProperty(FailedStagesField).EnumerateArray()
            .Count(stage => stage.GetString() == CaptureDisposeStage)).IsEqualTo(1);
    }

    private static async Task PublishCaptureFailureAsync(ComparisonProgressNativeFixture fixture, TaskCompletionSource observed)
    {
        var notifications = fixture.Application.Services.GetRequiredService<ResourceNotificationService>();
        await notifications.PublishUpdateAsync(fixture.Runner, snapshot => snapshot with { State = KnownResourceStates.Running });
        var logger = fixture.Application.Services.GetRequiredService<ResourceLoggerService>().GetLogger(fixture.Runner);
        WriteCaptureMarker(logger, CaptureMarker, null);
        using var deadline = new CancellationTokenSource(
            NativeExecutionPolicyFixture.Harness().Value.ConcurrentCaptureDisposalTimeout, TimeProvider.System);
        await observed.Task.WaitAsync(deadline.Token);
    }

    [Test]
    public async Task DuplicateTaskObservationPreservesDistinctStagesAndOriginalFailureOrder()
    {
        var primary = new OperationCanceledException(PrimaryMarker);
        var original = new IOException(CaptureMarker);
        var independent = new IOException(CaptureMarker);
        var failures = new IsolatedNativeTeardownFailures(primary);
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
        throw new InvalidOperationException("Native teardown did not propagate its primary and cleanup failures.");
    }
}
