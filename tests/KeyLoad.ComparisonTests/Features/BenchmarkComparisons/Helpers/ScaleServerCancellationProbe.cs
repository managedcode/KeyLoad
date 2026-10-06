using System.Runtime.ExceptionServices;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.BenchmarkComparisons;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.Comparisons;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class ScaleServerCancellationProbe
{
    private static readonly JsonSerializerOptions ResourceJson = new(JsonSerializerDefaults.Web);
    private const string ProbeDirectory = "cancellation-probe";
    private const string WorkerFile = "worker.json";
    private const string EvidenceFile = "server-resource-evidence.json";
    private const string SamplingMissing = "serverCpuRss";
    private const string KeyLoad = "KeyLoad";
    private const string Profile = "scaled-100k-c16";
    private const string Scenario = "DocumentWrite";
    private const int AdditionalSampleCount = 1;

    internal static async Task VerifyAsync(DistributedApplication app, ContainerResource[] containers,
        ComparisonWorkerSelection selection, string root, CancellationToken token)
    {
        if (!OperatingSystem.IsLinux() || selection.Target != KeyLoad || selection.NodeCount != 1
            || selection.Profile != Profile || selection.Scenario.ToString() != Scenario)
        {
            return;
        }

        var output = Path.Combine(root, ProbeDirectory);
        Directory.CreateDirectory(output);
        File.Copy(Path.Combine(root, "reports", WorkerFile), Path.Combine(output, WorkerFile));
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(token);
        var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
        var collector = new ScaleServerResourceEvidenceCollector(selection, output, applicationStopping: lifetime.ApplicationStopping,
            executionOptions: app.Services.GetRequiredService<IOptions<ScaleServerResourceOptions>>(),
            provenanceOptions: app.Services.GetRequiredService<IOptions<BenchmarkProvenanceOptions>>());
        var failures = new List<Exception>();
        try
        {
            await RunCancellationOwnedAsync(app, containers, caller, collector, failures, token);
        }
        finally
        {
            await ScaleServerCancellationProbeSettlement.CaptureAsync(collector.DisposeAsync().AsTask(), failures);
        }
        ThrowFailures(failures);
        await using var stream = File.OpenRead(Path.Combine(output, EvidenceFile));
        var evidence = await JsonSerializer.DeserializeAsync<ScaleServerResourceEvidence>(stream,
            ResourceJson, token)
            ?? throw new InvalidDataException("Canceled native server evidence was malformed.");
        await Assert.That(evidence.Qualified).IsFalse();
        await Assert.That(evidence.MissingEvidence).Contains(SamplingMissing);
        await Assert.That(evidence.Containers.Length).IsEqualTo(selection.NodeCount);
        await Assert.That(evidence.Containers.All(item => item.SampleCount >= ScaleServerResourceBounds.MinimumSamples)).IsTrue();
        await VerifyFailedWriteMemoizationAsync(app, containers, selection, root, token);
    }

    private static async Task SettleFailureAsync(Exception primary, CancellationTokenSource caller,
        ScaleServerResourceEvidenceCollector collector, Task observation)
    {
        var failures = new List<Exception> { primary };
        await ScaleServerCancellationProbeSettlement.CaptureAsync(caller.CancelAsync(), failures);
        await ScaleServerCancellationProbeSettlement.CaptureAsync(collector.CompleteAsync(observation), failures);
        ThrowFailures(failures);
    }

    private static void ThrowFailures(List<Exception> failures)
    {
        if (failures.Count == 0)
        { return; }
        var fatal = failures.Select(CqrsRuntimeFailures.FindFatal).FirstOrDefault(item => item is not null);
        var ordered = new List<Exception>();
        if (fatal is not null)
        {
            ordered.Add(fatal);
        }

        foreach (var failure in failures)
        {
            if (!ordered.Any(existing => ReferenceEquals(existing, failure)))
            {
                ordered.Add(failure);
            }
        }
        if (ordered.Count == 1)
        {
            ExceptionDispatchInfo.Capture(ordered[0]).Throw();
        }

        throw new AggregateException(ordered);
    }

    private static async Task StartObservationAsync(DistributedApplication app, ContainerResource[] containers,
        ScaleServerResourceEvidenceCollector collector, CancellationTokenSource caller)
        => await collector.StartAsync(containers,
            readinessToken => ScaleServerResourceReadiness.WaitAsync(app, containers, readinessToken), caller.Token);

    private static async Task RunCanceledObservationAsync(DistributedApplication app, CancellationTokenSource caller,
        ScaleServerResourceEvidenceCollector collector, Task observation, CancellationToken token)
    {
        await Task.Delay(app.Services.GetRequiredService<IOptions<ScaleServerResourceOptions>>().Value.Cadence
            * (ScaleServerResourceBounds.MinimumSamples + AdditionalSampleCount) + app.Services.GetRequiredService<IOptions<TestExecutionOptions>>().Value.ProcessSettlementTimeout, token);
        await caller.CancelAsync();
        var completion = collector.CompleteAsync(observation);
        await Assert.That(ReferenceEquals(completion, collector.CompleteAsync(observation))).IsTrue();
        await completion;
    }

    private static async Task VerifyFailedWriteMemoizationAsync(DistributedApplication app,
        ContainerResource[] containers, ComparisonWorkerSelection selection, string root, CancellationToken token)
    {
        var output = Path.Combine(root, "failed-write-probe");
        Directory.CreateDirectory(output);
        File.Copy(Path.Combine(root, "reports", WorkerFile), Path.Combine(output, WorkerFile));
        Directory.CreateDirectory(Path.Combine(output, EvidenceFile));
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(token);
        var collector = new ScaleServerResourceEvidenceCollector(selection, output,
            applicationStopping: app.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping,
            executionOptions: app.Services.GetRequiredService<IOptions<ScaleServerResourceOptions>>(),
            provenanceOptions: app.Services.GetRequiredService<IOptions<BenchmarkProvenanceOptions>>());
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
        ThrowFailures(failures);
    }

    private static async Task RunCancellationOwnedAsync(DistributedApplication app, ContainerResource[] containers,
        CancellationTokenSource caller, ScaleServerResourceEvidenceCollector collector, List<Exception> failures, CancellationToken token)
    {
        var observation = StartObservationAsync(app, containers, collector, caller);
        var body = RunCanceledObservationAsync(app, caller, collector, observation, token);
        try
        { await ScaleServerCancellationProbeSettlement.CaptureAsync(body, failures); }
        finally
        {
            await ScaleServerCancellationProbeSettlement.CaptureAsync(caller.CancelAsync(), failures);
            await ScaleServerCancellationProbeSettlement.CaptureAsync(collector.CompleteAsync(observation), failures);
        }
    }

    private static async Task<Exception?> RunFailedWriteOwnedAsync(DistributedApplication app, ContainerResource[] containers,
        CancellationTokenSource caller, ScaleServerResourceEvidenceCollector collector, string output, List<Exception> failures)
    {
        var observation = StartObservationAsync(app, containers, collector, caller);
        var body = VerifyFailedWriteAsync(app, containers, caller, collector, observation, output);
        try
        { await ScaleServerCancellationProbeSettlement.CaptureAsync(body, failures); }
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

    private static async Task<OperationCanceledException> CancelNativeResourceWaitAsync(
        DistributedApplication app, string resourceName, CancellationTokenSource caller)
    {
        var original = AspireResourceCompletion.WaitForExitAsync(app, resourceName, caller.Token);
        await caller.CancelAsync();
        try
        { await original; }
        catch (OperationCanceledException failure) { return failure; }
        throw new InvalidOperationException("The actual Aspire resource wait did not observe caller cancellation.");
    }

    private static async Task<AggregateException> CaptureSettlementFailureAsync(Exception primary,
        CancellationTokenSource caller, ScaleServerResourceEvidenceCollector collector, Task observation)
    {
        try
        { await SettleFailureAsync(primary, caller, collector, observation); }
        catch (AggregateException failure) { return failure; }
        throw new InvalidOperationException("Cancellation and native sidecar writer failures were not retained together.");
    }

    private static async Task<Exception> CaptureWriteFailureAsync(Task completion)
    {
        try
        { await completion; }
        catch (IOException failure) { return failure; }
        catch (UnauthorizedAccessException failure) { return failure; }
        throw new InvalidOperationException("The native resource-evidence writer accepted a directory as its sidecar.");
    }
}
