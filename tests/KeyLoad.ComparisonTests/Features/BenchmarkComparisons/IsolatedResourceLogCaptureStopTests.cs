using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>AC-ISO-006: native log capture has one joinable stop and retains output after stopping.</summary>
internal sealed class IsolatedResourceLogCaptureStopTests
{
    private const string ResourceName = "native-node";
    private const string ComparisonResourceName = "comparisons";
    private const string ContainerImage = "mcr.microsoft.com/dotnet/runtime:10.0";
    private const string LogMarker = "isolated-stop-log-retained";
    private const string LogMarkerTemplate = "{LogMarker}";
    private const string OutputDirectoryPrefix = "keyload-log-stop-";
    private const string ComparisonLogName = "comparison.log";
    private const string ResourceLogDirectory = "resources";
    private const string ResourceLogExtension = ".log";
    private const int ConcurrentCallers = 8;
    private const int LogMarkerEventId = 1;
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MarkerPollInterval = TimeSpan.FromMilliseconds(10);
    private static readonly Action<ILogger, string, Exception?> LogNativeMarker =
        LoggerMessage.Define<string>(LogLevel.Information,
            new EventId(LogMarkerEventId, nameof(LogNativeMarker)), LogMarkerTemplate);

    [Test]
    public async Task StopAsyncSharesCompletionAndWritersRetainNativeLogsAfterStop()
    {
        var outputRoot = Directory.CreateTempSubdirectory(OutputDirectoryPrefix);
        IsolatedResourceLogCaptureStopFixture? fixture = null;
        var failures = new List<Exception>();
        try
        {
            await CollectFailureAsync(async () =>
            {
                fixture = await IsolatedResourceLogCaptureStopFixture.CreateAsync(
                    ResourceName, ComparisonResourceName, ContainerImage);
                await VerifyCaptureAsync(fixture, outputRoot.FullName);
            }, failures);
        }
        finally
        {
            await CollectFailureAsync(() => fixture?.DisposeAsync().AsTask() ?? Task.CompletedTask, failures);
            await CollectFailureAsync(() =>
            {
                outputRoot.Delete(recursive: true);
                return Task.CompletedTask;
            }, failures);
        }

        ThrowFailures(failures);
    }

    private static async Task VerifyCaptureAsync(IsolatedResourceLogCaptureStopFixture fixture, string outputRoot)
    {
        using var timeout = new CancellationTokenSource(Deadline);
        await PublishNativeLogsAsync(fixture, timeout.Token);
        await JoinSharedStopAsync(fixture.Capture, timeout.Token);
        await VerifyWritersAsync(fixture.Capture, outputRoot, timeout.Token);
        await DisposeCaptureTwiceAsync(fixture.Capture, timeout.Token);
    }

    private static async Task PublishNativeLogsAsync(IsolatedResourceLogCaptureStopFixture fixture, CancellationToken token)
    {
        var loggerService = fixture.Application.Services.GetRequiredService<ResourceLoggerService>();
        var notifications = fixture.Application.Services.GetRequiredService<ResourceNotificationService>();
        var failures = new List<Exception>();
        var subscriber = IsolatedResourceLogSubscriberScopeFactory.Create(
            loggerService.WatchAnySubscribersAsync(token), token);
        try
        {
            await CollectFailureAsync(async () =>
            {
                await ActivateAndObserveAsync(notifications, fixture.Resource, subscriber, token);
                await ActivateAndObserveAsync(notifications, fixture.ComparisonResource, subscriber, token);
                LogNativeMarker(loggerService.GetLogger(fixture.Resource), LogMarker, null);
                LogNativeMarker(loggerService.GetLogger(fixture.ComparisonResource), LogMarker, null);
                await WaitForCapturedLinesAsync(fixture.Capture, token);
            }, failures);
        }
        finally
        {
            var subscriberDisposal = subscriber.DisposeAsync().AsTask();
            await CollectFailureAsync(() => subscriberDisposal, failures);
        }

        ThrowFailures(failures);
    }

    private static async Task ActivateAndObserveAsync<T>(ResourceNotificationService notifications,
        ContainerResource resource, IsolatedResourceLogSubscriberScope<T> subscriber, CancellationToken token)
    {
        var observed = subscriber.RegisterMove(subscriber.Enumerator.MoveNextAsync().AsTask());
        var publish = notifications.PublishUpdateAsync(resource, snapshot => snapshot with
        {
            State = KnownResourceStates.Running
        });
        _ = subscriber.RegisterPublication(publish);
        await AwaitBoundedAndObserveAsync(publish, token);
        await AwaitBoundedAndObserveAsync(observed, token);
    }

    private static async Task WaitForCapturedLinesAsync(ComparisonTestLogCapture capture, CancellationToken token)
    {
        while (!capture.HasCapturedLine(ResourceName, LogMarker)
            || !capture.HasCapturedLine(ComparisonResourceName, LogMarker))
        {
            await Task.Delay(MarkerPollInterval, token);
        }
        await Assert.That(capture.HasCapturedLine(ResourceName, LogMarker)).IsTrue();
        await Assert.That(capture.HasCapturedLine(ComparisonResourceName, LogMarker)).IsTrue();
    }

    private static async Task JoinSharedStopAsync(ComparisonTestLogCapture capture, CancellationToken token)
    {
        var callersReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var concurrentCalls = Enumerable.Range(0, ConcurrentCallers)
            .Select(_ => CallStopAsync(capture, callersReady.Task)).ToArray();
        callersReady.SetResult();
        var joinedTasks = await AwaitBoundedAndObserveAsync(Task.WhenAll(concurrentCalls), token);
        await Assert.That(joinedTasks.All(task => ReferenceEquals(task, joinedTasks[0]))).IsTrue();
        var consecutive = capture.StopAsync();
        await Assert.That(ReferenceEquals(joinedTasks[0], consecutive)).IsTrue();
        await AwaitBoundedAndObserveAsync(Task.WhenAll(joinedTasks), token);
    }

    private static async Task VerifyWritersAsync(ComparisonTestLogCapture capture, string outputRoot,
        CancellationToken token)
    {
        var comparisonLog = Path.Combine(outputRoot, ComparisonLogName);
        var resourceDirectory = Path.Combine(outputRoot, ResourceLogDirectory);
        Directory.CreateDirectory(resourceDirectory);
        await AwaitBoundedAndObserveAsync(capture.WriteToAsync(comparisonLog), token);
        await AwaitBoundedAndObserveAsync(capture.WriteResourcesToAsync(resourceDirectory), token);
        var retainedComparisonLog = await File.ReadAllTextAsync(comparisonLog, token);
        var retainedResourceLog = await File.ReadAllTextAsync(Path.Combine(resourceDirectory, ResourceName + ResourceLogExtension), token);
        await Assert.That(retainedComparisonLog.Contains(LogMarker, StringComparison.Ordinal)).IsTrue();
        await Assert.That(retainedResourceLog.Contains(LogMarker, StringComparison.Ordinal)).IsTrue();
    }

    private static async Task DisposeCaptureTwiceAsync(ComparisonTestLogCapture capture, CancellationToken token)
    {
        await AwaitBoundedAndObserveAsync(capture.DisposeAsync().AsTask(), token);
        await AwaitBoundedAndObserveAsync(capture.DisposeAsync().AsTask(), token);
    }

    private static async Task<Task> CallStopAsync(ComparisonTestLogCapture capture, Task callersReady)
    {
        await callersReady;
        return capture.StopAsync();
    }

    private static Task<T> AwaitBoundedAndObserveAsync<T>(Task<T> operation, CancellationToken token)
        => IsolatedResourceLogCaptureStopSupport.AwaitBoundedAndObserveAsync(operation, token);

    private static Task AwaitBoundedAndObserveAsync(Task operation, CancellationToken token)
        => IsolatedResourceLogCaptureStopSupport.AwaitBoundedAndObserveAsync(operation, token);

    private static Task CollectFailureAsync(Func<Task> action, List<Exception> failures)
        => IsolatedResourceLogCaptureStopSupport.CollectFailureAsync(action, failures);

    private static void ThrowFailures(List<Exception> failures)
        => IsolatedResourceLogCaptureStopSupport.ThrowFailures(failures);
}
