using System.Text.Json;
using Aspire.Hosting;
using KeyLoad.Comparisons;
using KeyLoad.LoadTests.AppHost.BenchmarkComparisons;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonHost.BenchmarkOrchestration;

/// <summary>Validates and retains original workload evidence before the final session disposition.</summary>
internal static class BenchmarkSessionQualifications
{
    private const int EmptyCount = 0;
    private const string PublicationSourceValue = "publication-source";
    private const string LocalExecutionHasNoWorkflowPublicationProvenanceValue = "LocalExecutionHasNoWorkflowPublicationProvenance";
    private const string ClientFailedValue = "ClientFailed";
    private const string RuntimeTopologyUnavailableValue = "RuntimeTopologyUnavailable";
    private const string QualificationFailedReason = "SessionQualificationFailed";
    private const int SingleItemCount = 1;

    internal const string FileName = "session-qualification.json";
    private const string Passed = "passed";
    private const string Failed = "failed";
    private const string NotApplicable = "notApplicable";
    private const string NotExecuted = "notExecuted";
    private const string Correctness = "execution-correctness";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static async Task<Exception?> RunAsync(DistributedApplication application,
        BenchmarkSessionSelection selection, BenchmarkSessionCallerOptions selected, string evidence,
        int runtimeExit, CancellationToken token)
    {
        var unsupported = BenchmarkSessionContract.UnsupportedTopologies.Any(item => item.Target == selection.Target
            && item.NodeCounts.Contains(selection.NodeCount));
        var steps = new List<Step>();
        var failures = new List<Exception>();
        if (runtimeExit == EmptyCount && !unsupported)
        {
            await InvokeAsync(Correctness, applicable: true,
                () => BenchmarkSessionManifest.ValidateOutputsAsync(evidence, selected, runtimeExit, token), steps, failures);
        }
        if (!string.Equals(application.Services.GetRequiredService<IOptions<BenchmarkProvenanceOptions>>().Value.Actions, bool.TrueString, StringComparison.OrdinalIgnoreCase))
        {
            steps.Add(new(PublicationSourceValue, NotApplicable, LocalExecutionHasNoWorkflowPublicationProvenanceValue));
        }
        var status = runtimeExit != EmptyCount ? NotExecuted : unsupported ? NotApplicable : failures.Count == EmptyCount ? Passed : Failed;
        var reason = runtimeExit != EmptyCount ? ClientFailedValue : unsupported ? RuntimeTopologyUnavailableValue
            : failures.Count == EmptyCount ? null : QualificationFailedReason;
        var report = new Report(SingleItemCount, selected.SessionId, selection.Target, selection.NodeCount, runtimeExit, status, reason, steps.ToArray());
        await WriteAsync(evidence, report);
        return failures.Count switch
        {
            EmptyCount => null,
            SingleItemCount => failures[EmptyCount],
            _ => new AggregateException(failures)
        };
    }

    private static async Task InvokeAsync(string id, bool applicable, Func<Task> operation,
        List<Step> steps, List<Exception> failures)
    {
        if (!applicable)
        {
            steps.Add(new(id, NotApplicable, null));
            return;
        }
        try
        {
            await operation();
            steps.Add(new(id, Passed, null));
        }
        catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is null)
        {
            failures.Add(failure);
            steps.Add(new(id, Failed, failure.GetType().Name));
        }
    }

    private static async Task WriteAsync(string evidence, Report report)
    {
        var execution = ExecutionPolicyFixture.Harness().Value;
        using var deadline = new CancellationTokenSource(execution.TeardownReceiptTimeout, TimeProvider.System);
        await using var file = new FileStream(Path.Combine(evidence, FileName), FileMode.CreateNew, FileAccess.Write,
            FileShare.None, execution.TeardownReceiptFileBufferBytes, FileOptions.Asynchronous);
        await JsonSerializer.SerializeAsync(file, report, Json, deadline.Token);
        await file.FlushAsync(deadline.Token);
    }

    private sealed record Step(string Id, string Status, string? FailureType);
    private sealed record Report(int SchemaVersion, string SessionId, string Target, int NodeCount, int RuntimeExitCode,
        string Status, string? Reason, Step[] Steps);
}
