using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class IsolatedNativeLogCaptureConcurrentDisposeTests
{
    private const string ResourceName = "native-node";
    private const string ComparisonResourceName = "comparisons";
    private const string ContainerImage = "mcr.microsoft.com/dotnet/runtime:10.0";
    private const string Marker = "concurrent-dispose-capture-marker";
    private const string MarkerTemplate = "{Marker}";
    private const string EventName = nameof(ConcurrentDisposeWaitsForOriginalCaptureJoin);
    private const string BodyFailureStage = "body";
    private const string SubscriberDisposeFailureStage = "subscriber-dispose";
    private const string CaptureDisposeFailureStage = "capture-dispose";
    private const string FixtureDisposeFailureStage = "fixture-dispose";
    private const int MarkerEventId = 2;
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(30);
    private static readonly Action<ILogger, string, Exception?> WriteMarker = LoggerMessage.Define<string>(
        LogLevel.Information, new EventId(MarkerEventId, EventName), MarkerTemplate);

    [Test]
    public async Task ConcurrentDisposeWaitsForOriginalCaptureJoin()
    {
        IsolatedResourceLogCaptureStopFixture? fixture = null;
        ComparisonTestLogCapture? capture = null;
        var callbackEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCallback = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failures = new IsolatedNativeTeardownFailures(null);
        try
        {
            using var timeout = new CancellationTokenSource(Deadline);
            fixture = await IsolatedResourceLogCaptureStopFixture.CreateAsync(
                ResourceName, ComparisonResourceName, ContainerImage);
            capture = CreateBlockedCapture(fixture, callbackEntered, releaseCallback);
            await PublishBlockedLineAsync(fixture, callbackEntered, timeout.Token);
            await VerifySharedDisposalAsync(capture, releaseCallback, timeout.Token);
        }
        catch (Exception failure)
        {
            failures.Record(BodyFailureStage, failure);
        }
        finally
        {
            releaseCallback.TrySetResult();
            await DisposeCaptureAsync(capture, failures);
            await DisposeFixtureAsync(fixture, failures);
        }

        failures.ThrowIfAny();
    }

    private static ComparisonTestLogCapture CreateBlockedCapture(IsolatedResourceLogCaptureStopFixture fixture,
        TaskCompletionSource entered, TaskCompletionSource release)
    {
        Action<string> observer = line => BlockMarker(line, entered, release);
        return new ComparisonTestLogCapture(fixture.Application, [ResourceName], nativeLineObserver: observer);
    }

    private static void BlockMarker(string line, TaskCompletionSource entered, TaskCompletionSource release)
    {
        if (line.Contains(Marker, StringComparison.Ordinal))
        {
            entered.TrySetResult();
            release.Task.GetAwaiter().GetResult();
        }
    }

    private static async Task PublishBlockedLineAsync(IsolatedResourceLogCaptureStopFixture fixture,
        TaskCompletionSource callbackEntered, CancellationToken token)
    {
        var logger = fixture.Application.Services.GetRequiredService<ResourceLoggerService>();
        var notifications = fixture.Application.Services.GetRequiredService<ResourceNotificationService>();
        var subscriber = IsolatedResourceLogSubscriberScopeFactory.Create(logger.WatchAnySubscribersAsync(token), token);
        Exception? primary = null;
        try
        {
            await ActivateAndObserveAsync(notifications, fixture.Resource, subscriber, token);
            await ActivateAndObserveAsync(notifications, fixture.ComparisonResource, subscriber, token);
            WriteMarker(logger.GetLogger(fixture.ComparisonResource), Marker, null);
            await IsolatedResourceLogCaptureStopSupport.AwaitBoundedAndObserveAsync(callbackEntered.Task, token);
        }
        catch (Exception failure)
        {
            primary = failure;
        }
        var failures = new IsolatedNativeTeardownFailures(primary);
        try { await subscriber.DisposeAsync(); }
        catch (Exception failure) { failures.Record(SubscriberDisposeFailureStage, failure); }
        failures.ThrowIfAny();
    }

    private static async Task ActivateAndObserveAsync<T>(ResourceNotificationService notifications,
        ContainerResource resource, IsolatedResourceLogSubscriberScope<T> subscriber, CancellationToken token)
    {
        var observed = subscriber.RegisterMove(subscriber.Enumerator.MoveNextAsync().AsTask());
        var publication = notifications.PublishUpdateAsync(resource, snapshot => snapshot with
        {
            State = KnownResourceStates.Running
        });
        _ = subscriber.RegisterPublication(publication);
        await IsolatedResourceLogCaptureStopSupport.AwaitBoundedAndObserveAsync(publication, token);
        await IsolatedResourceLogCaptureStopSupport.AwaitBoundedAndObserveAsync(observed, token);
    }

    private static async Task VerifySharedDisposalAsync(ComparisonTestLogCapture capture,
        TaskCompletionSource release, CancellationToken token)
    {
        var first = capture.DisposeAsync().AsTask();
        var second = capture.DisposeAsync().AsTask();
        await Assert.That(ReferenceEquals(first, second)).IsTrue();
        await Assert.That(first.IsCompleted).IsFalse();
        await Assert.That(second.IsCompleted).IsFalse();
        release.TrySetResult();
        await IsolatedResourceLogCaptureStopSupport.AwaitBoundedAndObserveAsync(
            Task.WhenAll(first, second), token);
    }

    private static async Task DisposeCaptureAsync(ComparisonTestLogCapture? capture,
        IsolatedNativeTeardownFailures failures)
    {
        if (capture is null) { return; }
        try { await capture.DisposeAsync(); }
        catch (Exception failure) { failures.Record(CaptureDisposeFailureStage, failure); }
    }

    private static async Task DisposeFixtureAsync(IsolatedResourceLogCaptureStopFixture? fixture,
        IsolatedNativeTeardownFailures failures)
    {
        if (fixture is null) { return; }
        try { await fixture.DisposeAsync(); }
        catch (Exception failure) { failures.Record(FixtureDisposeFailureStage, failure); }
    }
}
