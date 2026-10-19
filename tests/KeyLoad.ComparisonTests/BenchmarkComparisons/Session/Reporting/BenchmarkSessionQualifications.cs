using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.Comparisons;
using KeyLoad.LoadTests.AppHost.Features.BenchmarkComparisons;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Retains actual optional execution correctness invocations before the final session disposition.</summary>
internal static class BenchmarkSessionQualifications
{
    internal const string FileName = "session-qualification.json";
    private const string Passed = "passed";
    private const string Failed = "failed";
    private const string NotApplicable = "notApplicable";
    private const string NotExecuted = "notExecuted";
    private const string Cancellation = "server-resource-cancellation";
    private const string Correctness = "execution-correctness";
    private static readonly string[] CorrectnessTargets =
        ["KeyLoad", "KurrentDB", "PostgreSQL + pgvector", "Redis", "OpenSearch", "MongoDB", "Neo4j"];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static async Task<Exception?> RunAsync(DistributedApplication application, ContainerResource[] containers,
        BenchmarkSessionSelection selection, BenchmarkSessionCallerOptions selected, string evidence, string root,
        int runtimeExit, CancellationToken token)
    {
        var unsupported = BenchmarkSessionContract.UnsupportedTopologies.Any(item => item.Target == selection.Target
            && item.NodeCounts.Contains(selection.NodeCount));
        var steps = new List<Step>();
        var failures = new List<Exception>();
        if (runtimeExit == 0 && !unsupported)
        {
            await InvokeAsync(Cancellation, OperatingSystem.IsLinux() && selection.Target != "KeyLoad" && selection.NodeCount == 1,
                () => ScaleServerCancellationProbe.VerifyAsync(application, containers, selection, root, evidence, selected.SessionId, token), steps, failures);
            await InvokeAsync(Correctness, CorrectnessTargets.Contains(selection.Target, StringComparer.Ordinal),
                () => IsolatedCheckss.VerifyAsync(application, selection, evidence, token), steps, failures);
        }
        if (selection.Target == "KeyLoad" && !unsupported)
        {
            steps.Add(new("matched-execution-process-resources", NotExecuted, "MatchedProcessResourceQualificationOpen"));
        }
        if (!string.Equals(application.Services.GetRequiredService<IOptions<BenchmarkProvenanceOptions>>().Value.Actions, bool.TrueString, StringComparison.OrdinalIgnoreCase))
        {
            steps.Add(new("publication-source", NotApplicable, "LocalExecutionHasNoWorkflowPublicationProvenance"));
        }
        var status = runtimeExit != 0 ? NotExecuted : unsupported ? NotApplicable : failures.Count == 0 ? Passed : Failed;
        var reason = runtimeExit != 0 ? "ClientFailed" : unsupported ? "RuntimeTopologyUnavailable"
            : failures.Count == 0 ? null : "SessionQualificationFailed";
        var report = new Report(1, selected.SessionId, selection.Target, selection.NodeCount, runtimeExit, status, reason, steps.ToArray());
        await WriteAsync(evidence, report);
        return failures.Count switch
        {
            0 => null,
            1 => failures[0],
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
