using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>AC-BC-LIVE-002/003: real execution resource logs persist only runner markers before teardown.</summary>
internal sealed class ComparisonProgressCaptureTests
{
    private const string First = "KeyLoadBenchmarkProgress phase=measure repetition=1 completed=7 total=10 failed=2 elapsedSeconds=30.125";
    private const string Last = "KeyLoadBenchmarkProgress phase=complete repetition=1 completed=10 total=10 failed=2 elapsedSeconds=31";
    private const string PrivateLine = "private-document-payload";
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(30);
    private static readonly Action<ILogger, string, Exception?> LogMarker = LoggerMessage.Define<string>(
        LogLevel.Information, new EventId(1, nameof(LogMarker)), "{Marker}");

    [Test]
    public async Task RunnerSnapshotIsVisibleBeforeStopAndRetainedAfterObserverCancellation()
    {
        var root = ComparisonProgressFileTests.CreateRoot("keyload-progress-execution-");
        var evidence = Path.Combine(root.FullName, "workers", "keyload-n3-vector-exact");
        var progressPath = ComparisonProgressLine.PathForEvidenceDirectory(evidence);
        ComparisonProgressFixture? fixture = null;
        var failures = new List<Exception>();
        try
        {
            await IsolatedResourceLogCaptureStopSupport.CollectFailureAsync(async () =>
            {
                fixture = await ComparisonProgressFixture.CreateAsync(progressPath);
                await RuntimeVerifyAsync(fixture, progressPath);
            }, failures);
        }
        finally
        {
            await IsolatedResourceLogCaptureStopSupport.CollectFailureAsync(
                () => fixture?.DisposeAsync().AsTask() ?? Task.CompletedTask, failures);
            await IsolatedResourceLogCaptureStopSupport.CollectFailureAsync(() =>
            {
                root.Delete(recursive: true);
                return Task.CompletedTask;
            }, failures);
        }
        IsolatedResourceLogCaptureStopSupport.ThrowFailures(failures);
    }

    private static async Task RuntimeVerifyAsync(ComparisonProgressFixture fixture, string progressPath)
    {
        using var deadline = new CancellationTokenSource(Deadline, TimeProvider.System);
        var token = deadline.Token;
        await ObserveSubscribersAsync(fixture, token);
        var logger = fixture.Application.Services.GetRequiredService<ResourceLoggerService>();
        LogMarker(logger.GetLogger(fixture.Node), First, null);
        LogMarker(logger.GetLogger(fixture.Runner), PrivateLine, null);
        await WaitForCapturedAsync(fixture.Capture, ComparisonProgressFixture.NodeName, First, token);
        await WaitForCapturedAsync(fixture.Capture, ComparisonProgressFixture.RunnerName, PrivateLine, token);
        await Assert.That(File.Exists(progressPath)).IsFalse();
        LogMarker(logger.GetLogger(fixture.Runner), First, null);
        await ComparisonProgressFileTests.WaitForSnapshotAsync(progressPath, First, token);
        await Assert.That(File.Exists(progressPath)).IsTrue();
        LogMarker(logger.GetLogger(fixture.Runner), Last, null);
        await WaitForCapturedAsync(fixture.Capture, ComparisonProgressFixture.RunnerName, Last, token);
        await fixture.Capture.StopAsync().WaitAsync(token);
        await Assert.That((await File.ReadAllTextAsync(progressPath, token)).TrimEnd()).IsEqualTo(Last);
        var runtimeLog = Path.Combine(Path.GetDirectoryName(progressPath)!, "runner.log");
        await fixture.Capture.WriteToAsync(runtimeLog).WaitAsync(token);
        await Assert.That(await File.ReadAllTextAsync(runtimeLog, token)).Contains(PrivateLine);
        await Assert.That(await File.ReadAllTextAsync(progressPath, token)).DoesNotContain(PrivateLine);
    }

    private static async Task ObserveSubscribersAsync(ComparisonProgressFixture fixture, CancellationToken token)
    {
        var logger = fixture.Application.Services.GetRequiredService<ResourceLoggerService>();
        var notifications = fixture.Application.Services.GetRequiredService<ResourceNotificationService>();
        var subscriber = IsolatedResourceLogSubscriberScopeFactory.Create(logger.WatchAnySubscribersAsync(token), token);
        try
        {
            await ActivateAsync(notifications, fixture.Node, subscriber, token);
            await ActivateAsync(notifications, fixture.Runner, subscriber, token);
        }
        finally
        {
            await subscriber.DisposeAsync().AsTask().WaitAsync(token);
        }
    }

    private static async Task ActivateAsync<T>(ResourceNotificationService notifications, ContainerResource resource,
        IsolatedResourceLogSubscriberScope<T> subscriber, CancellationToken token)
    {
        var observed = subscriber.RegisterMove(subscriber.Enumerator.MoveNextAsync().AsTask());
        var publication = notifications.PublishUpdateAsync(resource, snapshot => snapshot with { State = KnownResourceStates.Running });
        _ = subscriber.RegisterPublication(publication);
        await IsolatedResourceLogCaptureStopSupport.AwaitBoundedAndObserveAsync(publication, token);
        await IsolatedResourceLogCaptureStopSupport.AwaitBoundedAndObserveAsync(observed, token);
    }

    private static async Task WaitForCapturedAsync(ComparisonTestLogCapture capture, string resource, string marker,
        CancellationToken token)
    {
        while (!capture.HasCapturedLine(resource, marker))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), TimeProvider.System, token);
        }
    }
}
