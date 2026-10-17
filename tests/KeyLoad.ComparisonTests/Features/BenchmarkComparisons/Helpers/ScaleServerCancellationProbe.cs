using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.BenchmarkComparisons;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.Comparisons;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class ScaleServerCancellationProbe
{
    private const string ProbeDirectory = "cancellation-probe";
    private const string WorkerFile = "session-state.json";
    private const string EvidenceFile = "session-resource-evidence.json";
    private const string SamplingMissing = "serverCpuRss";
    private const string KeyLoad = "KeyLoad";
    private const string SessionIdentityEnvironment = "KEYLOAD_BENCHMARK_SESSION_ID";
    private const string ObservationsField = "observations";
    private const string QualifiedField = "qualified";
    private const string MissingEvidenceField = "missingEvidence";
    private const string ContainersField = "containers";
    private const string SampleCountField = "sampleCount";
    private const int AdditionalSampleCount = 1;

    internal static async Task VerifyAsync(DistributedApplication app, ContainerResource[] containers,
        BenchmarkSessionSelection selection, string root, string evidenceDirectory, CancellationToken token)
    {
        if (!OperatingSystem.IsLinux() || selection.Target != KeyLoad || selection.NodeCount != 1)
        {
            return;
        }

        var output = Path.Combine(root, ProbeDirectory);
        Directory.CreateDirectory(output);
        File.Copy(Path.Combine(evidenceDirectory, WorkerFile), Path.Combine(output, WorkerFile));
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(token);
        var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
        var collector = new ScaleServerResourceEvidenceCollector(selection, output, applicationStopping: lifetime.ApplicationStopping,
            executionOptions: app.Services.GetRequiredService<IOptions<ScaleServerResourceOptions>>(),
            provenanceOptions: app.Services.GetRequiredService<IOptions<BenchmarkProvenanceOptions>>(),
            sessionId: ComparisonImageProtocol.RequiredEnvironment(SessionIdentityEnvironment),
            provider: app.Services.GetRequiredService<TimeProvider>());
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
        using var evidence = await JsonDocument.ParseAsync(stream, cancellationToken: token);
        var observations = evidence.RootElement.GetProperty(ObservationsField);
        await Assert.That(observations.GetProperty(QualifiedField).GetBoolean()).IsFalse();
        await Assert.That(observations.GetProperty(MissingEvidenceField).EnumerateArray()
            .Select(item => item.GetString())).Contains(SamplingMissing);
        var recorded = observations.GetProperty(ContainersField).EnumerateArray().ToArray();
        await Assert.That(recorded.Length).IsEqualTo(selection.NodeCount);
        await Assert.That(recorded.All(item => item.GetProperty(SampleCountField).GetInt32()
            >= ScaleServerResourceBounds.MinimumSamples)).IsTrue();
        await VerifyFailedWriteMemoizationAsync(app, containers, selection, root, evidenceDirectory, token);
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
        => ScaleServerProbeFailureOrder.Throw(failures);

    private static async Task StartObservationAsync(DistributedApplication app, ContainerResource[] containers,
        ScaleServerResourceEvidenceCollector collector, CancellationTokenSource caller)
        => await collector.StartAsync(containers,
            readinessToken => ScaleServerResourceReadiness.WaitAsync(app, containers, readinessToken), caller.Token);

    private static async Task RunCanceledObservationAsync(DistributedApplication app, CancellationTokenSource caller,
        ScaleServerResourceEvidenceCollector collector, Task observation, CancellationToken token)
    {
        await Task.Delay(app.Services.GetRequiredService<IOptions<ScaleServerResourceOptions>>().Value.Cadence
            * (ScaleServerResourceBounds.MinimumSamples + AdditionalSampleCount) + app.Services.GetRequiredService<IOptions<TestExecutionOptions>>().Value.ProcessSettlementTimeout, TimeProvider.System, token);
        await caller.CancelAsync();
        var completion = collector.CompleteAsync(observation);
        await Assert.That(ReferenceEquals(completion, collector.CompleteAsync(observation))).IsTrue();
        await completion;
    }

    private static async Task VerifyFailedWriteMemoizationAsync(DistributedApplication app,
        ContainerResource[] containers, BenchmarkSessionSelection selection, string root, string evidenceDirectory, CancellationToken token)
    {
        var output = Path.Combine(root, "failed-write-probe");
        Directory.CreateDirectory(output);
        File.Copy(Path.Combine(evidenceDirectory, WorkerFile), Path.Combine(output, WorkerFile));
        Directory.CreateDirectory(Path.Combine(output, EvidenceFile));
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(token);
        var collector = new ScaleServerResourceEvidenceCollector(selection, output,
            applicationStopping: app.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping,
            executionOptions: app.Services.GetRequiredService<IOptions<ScaleServerResourceOptions>>(),
            provenanceOptions: app.Services.GetRequiredService<IOptions<BenchmarkProvenanceOptions>>(),
            sessionId: ComparisonImageProtocol.RequiredEnvironment(SessionIdentityEnvironment),
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
        ThrowFailures(failures);
    }

    private static async Task RunCancellationOwnedAsync(DistributedApplication app, ContainerResource[] containers,
        CancellationTokenSource caller, ScaleServerResourceEvidenceCollector collector, List<Exception> failures, CancellationToken token)
    {
        var observation = StartObservationAsync(app, containers, collector, caller);
        var body = RunCanceledObservationAsync(app, caller, collector, observation, token);
        try
        {
            await ScaleServerCancellationProbeSettlement.CaptureAsync(body, failures);
        }
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
