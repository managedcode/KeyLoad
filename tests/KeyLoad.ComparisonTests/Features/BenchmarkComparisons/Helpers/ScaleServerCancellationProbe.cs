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

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class ScaleServerCancellationProbe
{
    private const string ProbeDirectory = "cancellation-probe";
    private const string WorkerFile = "worker.json";
    private const string EvidenceFile = "server-resource-evidence.json";
    private const string SamplingMissing = "serverCpuRss";
    private const string KeyLoad = "KeyLoad";
    private const string Profile = "scaled-100k-c16";
    private const string Scenario = "DocumentWrite";
    private const int CompletionSampleSeconds = ScaleServerResourceBounds.CadenceSeconds
        * (ScaleServerResourceBounds.MinimumSamples + 1) + 5;

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
        var collector = new ScaleServerResourceEvidenceCollector(selection, output, lifetime.ApplicationStopping);
        var observation = collector.StartAsync(containers,
            readinessToken => ScaleServerResourceReadiness.WaitAsync(app, containers, readinessToken), caller.Token);
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(CompletionSampleSeconds), token);
            caller.Cancel();
            var completion = collector.CompleteAsync(observation);
            await Assert.That(ReferenceEquals(completion, collector.CompleteAsync(observation))).IsTrue();
            await completion;
        }
        catch (Exception failure) when (IsNonFatal(failure))
        {
            await SettleFailureAsync(failure, caller, collector, observation);
        }
        catch (Exception failure) when (HasFatal(failure))
        {
            await SettleFailureAsync(failure, caller, collector, observation);
        }
        await using var stream = File.OpenRead(Path.Combine(output, EvidenceFile));
        var evidence = await JsonSerializer.DeserializeAsync<ScaleServerResourceEvidence>(stream,
            new JsonSerializerOptions(JsonSerializerDefaults.Web), token)
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
        try
        { caller.Cancel(); }
        catch (Exception failure) when (IsNonFatal(failure)) { failures.Add(failure); }
        catch (Exception failure) when (HasFatal(failure)) { failures.Add(failure); }
        try
        { await collector.CompleteAsync(observation); }
        catch (Exception failure) when (IsNonFatal(failure)) { failures.Add(failure); }
        catch (Exception failure) when (HasFatal(failure)) { failures.Add(failure); }
        ThrowFailures(failures);
    }

    private static void ThrowFailures(List<Exception> failures)
    {
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

    private static bool IsNonFatal(Exception failure) => CqrsRuntimeFailures.FindFatal(failure) is null;

    private static bool HasFatal(Exception failure) => CqrsRuntimeFailures.FindFatal(failure) is not null;

    private static async Task VerifyFailedWriteMemoizationAsync(DistributedApplication app,
        ContainerResource[] containers, ComparisonWorkerSelection selection, string root, CancellationToken token)
    {
        var output = Path.Combine(root, "failed-write-probe");
        Directory.CreateDirectory(output);
        File.Copy(Path.Combine(root, "reports", WorkerFile), Path.Combine(output, WorkerFile));
        Directory.CreateDirectory(Path.Combine(output, EvidenceFile));
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(token);
        var collector = new ScaleServerResourceEvidenceCollector(selection, output,
            app.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping);
        var observation = collector.StartAsync(containers,
            readinessToken => ScaleServerResourceReadiness.WaitAsync(app, containers, readinessToken), caller.Token);
        var primary = await CancelNativeResourceWaitAsync(app, containers[0].Name, caller);
        var settlement = await CaptureSettlementFailureAsync(primary, caller, collector, observation);
        var completion = collector.CompleteAsync(observation);
        var firstFailure = await CaptureWriteFailureAsync(completion);
        await Assert.That(ReferenceEquals(completion, collector.CompleteAsync(observation))).IsTrue();
        var repeatedFailure = await CaptureWriteFailureAsync(collector.CompleteAsync(observation));
        await Assert.That(ReferenceEquals(firstFailure, repeatedFailure)).IsTrue();
        await Assert.That(ReferenceEquals(primary, settlement.InnerExceptions[0])).IsTrue();
        await Assert.That(primary.CancellationToken).IsEqualTo(caller.Token);
        await Assert.That(caller.IsCancellationRequested).IsTrue();
        await Assert.That(observation.IsCompleted).IsTrue();
        await Assert.That(settlement.InnerExceptions.Any(item => ReferenceEquals(item, firstFailure))).IsTrue();
        await Assert.That(Directory.Exists(Path.Combine(output, EvidenceFile))).IsTrue();
    }

    private static async Task<OperationCanceledException> CancelNativeResourceWaitAsync(
        DistributedApplication app, string resourceName, CancellationTokenSource caller)
    {
        var original = AspireResourceCompletion.WaitForExitAsync(app, resourceName, caller.Token);
        caller.Cancel();
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
