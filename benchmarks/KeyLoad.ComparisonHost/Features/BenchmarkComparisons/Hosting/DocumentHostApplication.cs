using KeyLoad.Client;
using KeyLoad.Comparisons;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonHost.Features.BenchmarkComparisons;

/// <summary>Executes the common document family through native owners and retains each original repetition.</summary>
internal static class DocumentHostApplication
{
    internal const string WorkerFile = "document-worker.json";
    private const int SchemaVersion = 1;
    private const string Kind = "document-worker.v1";
    private const string Measured = "measured";
    private const string Failed = "failed";
    private const string Unsupported = "unsupported";
    private const string UnsupportedTopology = "unsupportedTopology";
    private const string UnsupportedOperation = " does not implement ";
    private const string NativeOperationSuffix = " natively.";
    private const string GuidFormat = "N";

    internal static async Task<int> RunAsync(IsolatedHostSettings settings,
        IOptions<NativeComparisonExecutionOptions> execution, IOptions<NativeComparisonDiagnosticOptions> diagnostics,
        IOptions<NativeComparisonSerializationOptions> serialization, IOptions<IsolatedKeyLoadAdmissionOptions> admission,
        IOptions<ComparisonLifecycleOptions> lifecycle, IOptions<KeyLoadClientExecutionOptions> client,
        IOptions<QueryTranslationOptions> translation, TimeProvider clock, CancellationToken token)
    {
        var selection = settings.Selection.DocumentWorkload ?? throw new InvalidOperationException(IsolatedHostConstants.Failure);
        async Task<IComparisonTarget> CreateAsync(int namespaceIndex, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            await using var owner = new IsolatedHostTargetOwner(execution, diagnostics, serialization, admission,
                lifecycle, client, translation, clock);
            _ = owner.Create(settings with { RunId = Guid.NewGuid().ToString(GuidFormat) });
            return owner.TransferTarget();
        }
        var progress = new DocumentHostProgress(execution, clock);
        var runner = new DocumentComparisonRunner(execution, clock, progress.Observe);
        var report = await runner.RunAsync(CreateAsync, selection, token);
        Validate(report, settings.Selection);
        var disposition = report.Status == Unsupported ? Unsupported : report.Qualified ? Measured : Failed;
        var reason = disposition == Unsupported
            ? settings.Selection.Target + UnsupportedOperation + selection.Scenario + NativeOperationSuffix : null;
        using var evidence = new CancellationTokenSource(execution.Value.CleanupTimeout, clock);
        await WriteAsync(settings, disposition, reason, disposition == Unsupported ? null : report, evidence.Token);
        return disposition == Failed ? ComparisonHostConstants.FailedExitCode : ComparisonHostConstants.SuccessfulExitCode;
    }

    internal static async Task<int> WriteUnavailableAsync(IsolatedHostSettings settings, string reason, CancellationToken token)
    {
        await WriteAsync(settings, UnsupportedTopology, reason, null, token);
        return ComparisonHostConstants.SuccessfulExitCode;
    }

    private static Task WriteAsync(IsolatedHostSettings settings, string disposition, string? reason,
        DocumentComparisonReport? report, CancellationToken token)
    {
        var selection = settings.Selection.DocumentWorkload ?? throw new InvalidOperationException(IsolatedHostConstants.Failure);
        var envelope = new DocumentWorkerEnvelope(SchemaVersion, Kind, settings.Worker,
            new(selection.Scenario, selection.DatasetRecords, selection.Clients), disposition, reason, report);
        return IsolatedHostReportWriter.WriteEnvelopeAsync(envelope, settings.OutputDirectory, WorkerFile,
            settings.HostExecution, token);
    }

    private static void Validate(DocumentComparisonReport report, ComparisonWorkerSelection selection)
    {
        if (report.Selection != selection.DocumentWorkload
            || report.Repetitions.Any(repetition => repetition.Target is { } target && (target.Name != selection.Target
                || repetition.Qualified && (target.Cluster is not { } cluster
                    || cluster.Nodes != selection.NodeCount || cluster.DataCopies != selection.NodeCount))))
        {
            throw new ComparisonFailureException(IsolatedHostConstants.Failure);
        }
    }

    private sealed record DocumentWorkerEnvelope(int SchemaVersion, string Kind, IsolatedComparisonWorker Worker,
        DocumentSelector Document, string Disposition, string? Reason, DocumentComparisonReport? Report);
    private sealed record DocumentSelector(DocumentComparisonScenario Scenario, int Records, int Clients);
}
