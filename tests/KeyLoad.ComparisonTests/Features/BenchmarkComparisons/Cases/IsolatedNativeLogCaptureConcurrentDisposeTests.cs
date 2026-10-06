using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
    private static readonly Action<ILogger, string, Exception?> WriteMarker = LoggerMessage.Define<string>(
        LogLevel.Information, new EventId(MarkerEventId, EventName), MarkerTemplate);

    [Test]
    public async Task ConcurrentDisposeWaitsForOriginalCaptureJoin()
    {
        var callbackEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCallback = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failures = new IsolatedNativeTeardownFailures(null);
        var execution = NativeExecutionPolicyFixture.Harness();
        var original = RunFixtureAsync(callbackEntered, releaseCallback, failures, execution);
        await IsolatedNativeOriginalTaskSettlement.RunAsync(() => original,
            FixtureDisposeFailureStage, failures, execution);
        failures.ThrowIfAny();
    }

    private static async Task RunFixtureAsync(TaskCompletionSource entered, TaskCompletionSource release,
        IsolatedNativeTeardownFailures failures, IOptions<NativeComparisonHarnessOptions> execution)
    {
        await using var fixture = await IsolatedResourceLogCaptureStopFixture.CreateAsync(
            ResourceName, ComparisonResourceName, ContainerImage);
        var original = RunCaptureAsync(fixture, entered, release, failures, execution);
        await IsolatedNativeOriginalTaskSettlement.RunAsync(() => original,
            CaptureDisposeFailureStage, failures, execution);
    }

    private static async Task RunCaptureAsync(IsolatedResourceLogCaptureStopFixture fixture,
        TaskCompletionSource entered, TaskCompletionSource release, IsolatedNativeTeardownFailures failures,
        IOptions<NativeComparisonHarnessOptions> execution)
    {
        Action<string> observer = line => BlockMarker(line, entered, release);
        await using var capture = new ComparisonTestLogCapture(fixture.Application, execution,
            [ResourceName], nativeLineObserver: observer);
        using var timeout = new CancellationTokenSource(execution.Value.ConcurrentCaptureDisposalTimeout);
        async Task RunBodyAsync()
        {
            await PublishBlockedLineAsync(fixture, entered, execution, timeout.Token);
            await VerifySharedDisposalAsync(capture, release, timeout.Token);
        }
        try
        {
            await IsolatedNativeOriginalTaskSettlement.RunAsync(RunBodyAsync, BodyFailureStage, failures, execution);
        }
        finally
        { release.TrySetResult(); }
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
        TaskCompletionSource callbackEntered, IOptions<NativeComparisonHarnessOptions> execution, CancellationToken token)
    {
        var failures = new IsolatedNativeTeardownFailures(null);
        async Task PublishScopeAsync()
        {
            var logger = fixture.Application.Services.GetRequiredService<ResourceLoggerService>();
            var notifications = fixture.Application.Services.GetRequiredService<ResourceNotificationService>();
            await using var subscriber = IsolatedResourceLogSubscriberScopeFactory.Create(
                logger.WatchAnySubscribersAsync(token), token);
            async Task PublishOwnedAsync()
            {
                await ActivateAndObserveAsync(notifications, fixture.Resource, subscriber, token);
                await ActivateAndObserveAsync(notifications, fixture.ComparisonResource, subscriber, token);
                WriteMarker(logger.GetLogger(fixture.ComparisonResource), Marker, null);
                await IsolatedResourceLogCaptureStopSupport.AwaitBoundedAndObserveAsync(callbackEntered.Task, token);
            }
            await IsolatedNativeOriginalTaskSettlement.RunAsync(PublishOwnedAsync, BodyFailureStage, failures, execution);
        }
        await IsolatedNativeOriginalTaskSettlement.RunAsync(PublishScopeAsync,
            SubscriberDisposeFailureStage, failures, execution);
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

}
