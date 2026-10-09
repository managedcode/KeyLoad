using System.Text.Json;
using KeyLoad.Comparisons;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class DocumentNativeReportAssertions
{
    private const string ClientsProperty = "clients";
    private const string DispositionProperty = "disposition";
    private const string DocumentProperty = "document";
    private const string JobIdProperty = "jobId";
    private const string KindProperty = "kind";
    private const string NodeCountProperty = "nodeCount";
    private const string ProfileProperty = "profile";
    private const string ReasonProperty = "reason";
    private const string RecordsProperty = "records";
    private const string ReportProperty = "report";
    private const string ScenarioProperty = "scenario";
    private const string SchemaVersionProperty = "schemaVersion";
    private const string SourceRevisionProperty = "sourceRevision";
    private const string TargetProperty = "target";
    private const string WorkerProperty = "worker";
    private const string FileName = "document-worker.json";
    private const string Kind = "document-worker.v1";
    private const string Measured = "measured";
    private const string Unsupported = "unsupported";
    private const string UnsupportedTopology = "unsupportedTopology";
    private const int SchemaVersion = 1;
    private const int EmptyCount = 0;
    private const string ShaEnvironment = "GITHUB_SHA";
    private const string JobEnvironment = "KEYLOAD_COMPARISON_JOB_ID";

    internal static async Task VerifyAsync(string output, ComparisonWorkerSelection selection, CancellationToken token)
    {
        await using var stream = File.OpenRead(Path.Combine(output, FileName));
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: token);
        var root = json.RootElement;
        var documents = selection.DocumentWorkload ?? throw new InvalidOperationException(Kind);
        await Assert.That(root.GetProperty(SchemaVersionProperty).GetInt32()).IsEqualTo(SchemaVersion);
        await Assert.That(root.GetProperty(KindProperty).GetString()).IsEqualTo(Kind);
        var worker = root.GetProperty(WorkerProperty);
        await Assert.That(worker.GetProperty(TargetProperty).GetString()).IsEqualTo(selection.Target);
        await Assert.That(worker.GetProperty(NodeCountProperty).GetInt32()).IsEqualTo(selection.NodeCount);
        await Assert.That(worker.GetProperty(ProfileProperty).GetString()).IsEqualTo(selection.Profile);
        await Assert.That(worker.GetProperty(SourceRevisionProperty).GetString()).IsEqualTo(Environment.GetEnvironmentVariable(ShaEnvironment));
        await Assert.That(worker.GetProperty(JobIdProperty).GetInt64().ToString(System.Globalization.CultureInfo.InvariantCulture))
            .IsEqualTo(Environment.GetEnvironmentVariable(JobEnvironment));
        var selector = root.GetProperty(DocumentProperty);
        await Assert.That(selector.GetProperty(ScenarioProperty).GetString()).IsEqualTo(documents.Scenario.ToString());
        await Assert.That(selector.GetProperty(RecordsProperty).GetInt32()).IsEqualTo(documents.DatasetRecords);
        await Assert.That(selector.GetProperty(ClientsProperty).GetInt32()).IsEqualTo(documents.Clients);
        var disposition = root.GetProperty(DispositionProperty).GetString();
        if (disposition is Unsupported or UnsupportedTopology)
        {
            await Assert.That(root.GetProperty(ReportProperty).ValueKind).IsEqualTo(JsonValueKind.Null);
            await Assert.That(root.GetProperty(ReasonProperty).GetString()).IsNotNull();
            return;
        }
        await Assert.That(disposition).IsEqualTo(Measured);
        var report = root.GetProperty(ReportProperty).Deserialize<DocumentComparisonReport>(ReportWriter.JsonOptions)
            ?? throw new InvalidDataException(Kind);
        await Assert.That(report.Qualified).IsTrue();
        await Assert.That(report.Selection).IsEqualTo(documents);
        await Assert.That(report.Repetitions.Length).IsEqualTo(DocumentComparisonContract.Current.Repetitions);
        foreach (var repetition in report.Repetitions)
        {
            await VerifyRepetitionAsync(repetition, selection);
        }
    }

    private static async Task VerifyRepetitionAsync(DocumentComparisonRepetition repetition, ComparisonWorkerSelection selection)
    {
        var documents = selection.DocumentWorkload!;
        await Assert.That(repetition.Qualified).IsTrue();
        await Assert.That(repetition.Errors).IsEmpty();
        await Assert.That(repetition.Target!.Cluster!.Nodes).IsEqualTo(selection.NodeCount);
        await Assert.That(repetition.Target.Cluster.DataCopies).IsEqualTo(selection.NodeCount);
        await Assert.That(repetition.RequestedClients).IsEqualTo(documents.Clients);
        await Assert.That(repetition.OpenedClients).IsEqualTo(documents.Clients);
        await Assert.That(repetition.PeakInFlight).IsLessThanOrEqualTo(documents.Clients);
        await Assert.That(repetition.Attempts).IsEqualTo((long)documents.Operations);
        await Assert.That(repetition.Acknowledged).IsEqualTo(repetition.Attempts);
        await Assert.That(repetition.Failed + repetition.Canceled + repetition.Unfinished).IsEqualTo((long)EmptyCount);
        await Assert.That(repetition.ExpectedFinalRecords).IsEqualTo(repetition.ActualFinalRecords);
        await Assert.That(repetition.ExpectedSha256).IsEqualTo(repetition.ActualSha256);
        await Assert.That(repetition.Histogram.Count).IsEqualTo(repetition.Attempts);
        await Assert.That(repetition.Histogram.BucketCounts.Sum()).IsEqualTo(repetition.Histogram.Count);
        await Assert.That(repetition.Histogram.OverflowCount).IsEqualTo((long)EmptyCount);
        await Assert.That(repetition.SingleOperationTiming).IsTrue();
        await Assert.That(repetition.Phases.PhaseInstrumentationComplete).IsTrue();
        await Assert.That(repetition.Phases.LoadSeconds).IsNotNull();
        await Assert.That(repetition.Phases.IndexBuildSeconds).IsNotNull();
    }
}
