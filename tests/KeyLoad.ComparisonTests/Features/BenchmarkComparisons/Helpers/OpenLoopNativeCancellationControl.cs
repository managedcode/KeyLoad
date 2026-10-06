using KeyLoad.Comparisons;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class OpenLoopNativeCancellationControl : IAsyncDisposable
{
    private const long FirstNativeCompletions = 1_024;
    private const string InvalidMarker = "OpenLoopNativeCompletionIdentityInvalid";
    private const string MissingMarker = "OpenLoopNativeCompletionNotObserved";
    private const string CancelFailureStage = "control-cancel";
    private const string JoinFailureStage = "control-join";
    private const string DisposeFailureStage = "control-lifetime-dispose";
    private readonly IsolatedNativeCasePlan plan;
    private readonly IOptions<NativeComparisonHarnessOptions> executionOptions;
    private readonly CancellationTokenSource lifetime;
    private readonly TaskCompletionSource<OpenLoopNativeCompletionV1> marker =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly System.Threading.Lock disposeGate = new();
    private Task? disposal;

    internal OpenLoopNativeCancellationControl(IsolatedNativeCasePlan plan, string output,
        IOptions<NativeComparisonHarnessOptions> executionOptions, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(executionOptions);
        executionOptions.Value.Validate();
        this.executionOptions = executionOptions;
        this.plan = plan;
        lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        Completion = PublishAsync(output);
    }

    internal Task Completion { get; }
    internal Task<OpenLoopNativeCompletionV1> ObservedMarker => marker.Task;

    internal void Observe(string line)
    {
        if (!line.StartsWith(OpenLoopCancellationProofContract.CompletionMarkerPrefix, StringComparison.Ordinal))
        {
            return;
        }
        if (!OpenLoopNativeCompletionMarker.TryParse(line, out var actual) || actual is null
            || actual.ProfileId != plan.Selection.Profile || actual.Scenario != plan.Selection.Scenario
            || actual.Rate != plan.Selection.OpenLoopRate || actual.Completed != FirstNativeCompletions)
        {
            marker.TrySetException(new InvalidDataException(InvalidMarker));
            return;
        }
        marker.TrySetResult(actual);
    }

    internal void RequirePublishedRequest()
    {
        if (!Completion.IsCompleted)
        {
            marker.TrySetException(new InvalidDataException(MissingMarker));
        }
    }

    private async Task PublishAsync(string output)
    {
        await marker.Task.WaitAsync(lifetime.Token);
        lifetime.Token.ThrowIfCancellationRequested();
        await OpenLoopCancellationRequestPublisher.PublishAsync(output, executionOptions);
    }

    public ValueTask DisposeAsync()
    {
        lock (disposeGate)
        {
            if (disposal is null)
            {
                var registered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                disposal = DisposeAndJoinAsync(registered.Task);
                registered.SetResult();
            }
            return new ValueTask(disposal);
        }
    }

    private async Task DisposeAndJoinAsync(Task registered)
    {
        await registered;
        var failures = new IsolatedNativeTeardownFailures(null);
        var execution = executionOptions;
        var original = DisposeOwnedLifetimeAsync(failures, execution);
        await IsolatedNativeOriginalTaskSettlement.RunAsync(() => original, DisposeFailureStage, failures, execution);
        failures.ThrowIfAny();
    }

    private async Task DisposeOwnedLifetimeAsync(IsolatedNativeTeardownFailures failures,
        IOptions<NativeComparisonHarnessOptions> execution)
    {
        using (lifetime)
        {
            await IsolatedNativeOriginalTaskSettlement.RunAsync(lifetime.CancelAsync, CancelFailureStage, failures, execution);
            await JoinPublicationAsync(failures);

        }
    }
    private async Task JoinPublicationAsync(IsolatedNativeTeardownFailures failures)
    {
        try
        { await Completion; }
        catch (OperationCanceledException failure) when (lifetime.IsCancellationRequested
            && failure.CancellationToken == lifetime.Token)
        { }
        catch (Exception failure) when (Completion.IsFaulted || Completion.IsCanceled)
        { RecordPublicationFailures(failure, failures); }
    }

    private void RecordPublicationFailures(Exception failure, IsolatedNativeTeardownFailures failures)
    {
        failures.Record(JoinFailureStage, failure);
        if (Completion.Exception is not { } aggregate)
        { return; }
        AddPublicationFailures(aggregate.InnerExceptions, failures);
    }

    private static void AddPublicationFailures(IEnumerable<Exception> originals,
        IsolatedNativeTeardownFailures failures)
    {
        foreach (var original in originals)
        { failures.Record(JoinFailureStage, original); }
    }

}
