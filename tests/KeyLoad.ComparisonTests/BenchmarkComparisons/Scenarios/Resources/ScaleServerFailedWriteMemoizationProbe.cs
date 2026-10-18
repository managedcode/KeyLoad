using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.LoadTests.AppHost.Features.BenchmarkComparisons;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.Comparisons;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Verifies original failed-write task and exception memoization against the actual native resource collector.</summary>
internal static class ScaleServerFailedWriteMemoizationProbe
{
    private const string ProbeDirectory = "failed-write-probe";
    private const string WorkerFile = "session-state.json";
    private const string EvidenceFile = "session-resource-evidence.json";

    internal static async Task VerifyAsync(DistributedApplication app,
        ContainerResource[] containers, BenchmarkSessionSelection selection, string root, string evidenceDirectory, string sessionId, CancellationToken token)
    {
        var output = Path.Combine(root, ProbeDirectory);
        Directory.CreateDirectory(output);
        File.Copy(Path.Combine(evidenceDirectory, WorkerFile), Path.Combine(output, WorkerFile));
        Directory.CreateDirectory(Path.Combine(output, EvidenceFile));
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(token);
        var collector = new ScaleServerResourceEvidenceCollector(selection, output,
            applicationStopping: app.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping,
            executionOptions: app.Services.GetRequiredService<IOptions<ScaleServerResourceOptions>>(),
            provenanceOptions: app.Services.GetRequiredService<IOptions<BenchmarkProvenanceOptions>>(),
            sessionId: sessionId,
            provider: app.Services.GetRequiredService<TimeProvider>());
        var failures = new List<Exception>();
        Exception? expected = null;
        try
        {
            expected = await RunFailedWriteOwnedAsync(app, containers, caller, collector, output, failures);
        }
        finally
        {
            await ScaleServerCancellationProbeSettlement.CaptureAsync(collector.DisposeAsync().AsTask(), failures);
        }
        if (expected is not null)
        {
            failures.RemoveAll(failure => ReferenceEquals(failure, expected));
        }
        ScaleServerProbeFailureOrder.Throw(failures);
    }

    private static async Task<Exception?> RunFailedWriteOwnedAsync(DistributedApplication app, ContainerResource[] containers,
        CancellationTokenSource caller, ScaleServerResourceEvidenceCollector collector, string output, List<Exception> failures)
    {
        var observation = ScaleServerCancellationProbe.StartObservationAsync(app, containers, collector, caller);
        var body = VerifyFailedWriteAsync(app, containers, caller, collector, observation, output);
        try
        {
            await ScaleServerCancellationProbeSettlement.CaptureAsync(body, failures);
        }
        finally
        {
            await ScaleServerCancellationProbeSettlement.CaptureAsync(caller.CancelAsync(), failures);
            await ScaleServerCancellationProbeSettlement.CaptureAsync(collector.CompleteAsync(observation), failures);
        }
        return body.IsCompletedSuccessfully ? await body : null;
    }

    private static async Task<Exception> VerifyFailedWriteAsync(DistributedApplication app, ContainerResource[] containers,
        CancellationTokenSource caller, ScaleServerResourceEvidenceCollector collector, Task observation, string output)
    {
        var primary = await CancelNativeResourceWaitAsync(app, containers[0].Name, caller);
        var settlement = await CaptureSettlementFailureAsync(primary, caller, collector, observation);
        var completion = collector.CompleteAsync(observation);
        var firstFailure = await CaptureWriteFailureAsync(completion);
        await Assert.That(ReferenceEquals(completion, collector.CompleteAsync(observation))).IsTrue();
        var repeatedFailure = await CaptureWriteFailureAsync(collector.CompleteAsync(observation));
        var disposalFailure = await CaptureWriteFailureAsync(collector.DisposeAsync().AsTask());
        await Assert.That(ReferenceEquals(firstFailure, repeatedFailure)).IsTrue();
        await Assert.That(ReferenceEquals(firstFailure, disposalFailure)).IsTrue();
        await Assert.That(ReferenceEquals(primary, settlement.InnerExceptions[0])).IsTrue();
        await Assert.That(primary.CancellationToken).IsEqualTo(caller.Token);
        await Assert.That(caller.IsCancellationRequested).IsTrue();
        await Assert.That(observation.IsCompleted).IsTrue();
        await Assert.That(settlement.InnerExceptions.Any(item => ReferenceEquals(item, firstFailure))).IsTrue();
        await Assert.That(Directory.Exists(Path.Combine(output, EvidenceFile))).IsTrue();
        return firstFailure;
    }

    private static async Task SettleFailureAsync(Exception primary, CancellationTokenSource caller,
        ScaleServerResourceEvidenceCollector collector, Task observation)
    {
        var failures = new List<Exception> { primary };
        await ScaleServerCancellationProbeSettlement.CaptureAsync(caller.CancelAsync(), failures);
        await ScaleServerCancellationProbeSettlement.CaptureAsync(collector.CompleteAsync(observation), failures);
        ScaleServerProbeFailureOrder.Throw(failures);
    }

    private static async Task<OperationCanceledException> CancelNativeResourceWaitAsync(
        DistributedApplication app, string resourceName, CancellationTokenSource caller)
    {
        var original = AspireResourceCompletion.WaitForExitAsync(app, resourceName, caller.Token);
        await caller.CancelAsync();
        try
        {
            await original;
        }
        catch (OperationCanceledException failure)
        {
            return failure;
        }
        throw new InvalidOperationException("The actual Aspire resource wait did not observe caller cancellation.");
    }

    private static async Task<AggregateException> CaptureSettlementFailureAsync(Exception primary,
        CancellationTokenSource caller, ScaleServerResourceEvidenceCollector collector, Task observation)
    {
        try
        {
            await SettleFailureAsync(primary, caller, collector, observation);
        }
        catch (AggregateException failure)
        {
            return failure;
        }
        throw new InvalidOperationException("Cancellation and native sidecar writer failures were not retained together.");
    }

    private static async Task<Exception> CaptureWriteFailureAsync(Task completion)
    {
        try
        {
            await completion;
        }
        catch (IOException failure)
        {
            return failure;
        }
        catch (UnauthorizedAccessException failure)
        {
            return failure;
        }
        throw new InvalidOperationException("The native resource-evidence writer accepted a directory as its sidecar.");
    }
}
