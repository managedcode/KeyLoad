using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeyLoad.Comparisons;

/// <summary>Writes the comparison report files and formats the existing Markdown summary.</summary>
public static class ReportWriter
{
    private const string MetricFormat = "F3";
    private const string JsonFileName = "results.json";
    private const string MarkdownFileName = "results.md";
    private const string CsvFileName = "samples.csv";

    /// <summary>Gets the published JSON serializer settings for raw comparison results.</summary>
    public static JsonSerializerOptions JsonOptions { get; } = CreateJsonOptions();
    /// <summary>Writes JSON, Markdown, and CSV reports to the specified directory.</summary>
    public static async Task WriteAsync(ComparisonReport report, string directory, IOptions<NativeComparisonExecutionOptions> executionOptions, CancellationToken cancellationToken)
    {
        var execution = NativeComparisonExecutionOptions.Require(executionOptions).Value;
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(report);
        report.ValidateConfiguration();
        ArgumentNullException.ThrowIfNull(directory);
        Directory.CreateDirectory(directory);
        cancellationToken.ThrowIfCancellationRequested();
        var jsonPath = Path.Combine(directory, JsonFileName);
        await using (var stream = new FileStream(jsonPath, new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.None,
            BufferSize = execution.ReportFileBufferBytes,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        }))
        {
            await JsonSerializer.SerializeAsync(stream, StreamedComparisonReport.Create(report), JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        cancellationToken.ThrowIfCancellationRequested();
        await File.WriteAllTextAsync(Path.Combine(directory, MarkdownFileName), Markdown(report), cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await ReportCsvWriter.WriteAsync(report, Path.Combine(directory, CsvFileName), Number, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Formats the established case summary for the console and Markdown file.</summary>
    public static string Markdown(ComparisonReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        report.ValidateConfiguration();
        var text = new StringBuilder(ReportWriterValues.KeyLoadComparisonResults);
        if (report.Options is { } options)
        {
            text.AppendLine(CultureInfo.CurrentCulture, $"{ReportWriterValues.Run}{report.RunId}{ReportWriterValues.Seed}{options.Seed}{ReportWriterValues.CorpusSHA256}{report.DatasetSha256}{ReportWriterValues.DigestCodeEnd}");
            text.AppendLine(CultureInfo.CurrentCulture, $"{ReportWriterValues.LineBreak}{options.Documents}{ReportWriterValues.Documents}{options.PayloadBytes}{ReportWriterValues.UTF8BytesDocument}{options.Dimensions}{ReportWriterValues.Float32DimensionsTop}{options.TopK}{ReportWriterValues.ListSeparator}{options.Operations}{ReportWriterValues.AttemptsCaseConcurrency}{options.Concurrency}{ReportWriterValues.ListSeparator}{options.Warmup}{ReportWriterValues.WarmupRequests}{options.Repetitions}{ReportWriterValues.Repetitions}");
            text.AppendLine(CultureInfo.CurrentCulture, $"{ReportWriterValues.Graph}{Math.Min(options.Documents, options.GraphVertices)}{ReportWriterValues.VerticesInTwoDisconnectedCyclicComponents}{options.GraphFanOut}{ReportWriterValues.TraversalDepth}{options.GraphDepth}{ReportWriterValues.NeighborsTraversalReturnSortedDistinctReachable}");
        }
        else if (report.ScaledProfile is { } scaled)
        {
            text.AppendLine(CultureInfo.CurrentCulture, $"{ReportWriterValues.Run}{report.RunId}{ReportWriterValues.Profile}{scaled.Id}{ReportWriterValues.Seed}{scaled.Seed}{ReportWriterValues.CorpusSHA256}{report.DatasetSha256}{ReportWriterValues.DigestCodeEnd}");
            text.AppendLine(CultureInfo.CurrentCulture, $"{ReportWriterValues.LineBreak}{scaled.Documents}{ReportWriterValues.NativeDocuments}{scaled.PayloadBytes}{ReportWriterValues.UTF8BytesDocument}{scaled.Operations}{ReportWriterValues.MeasuredOperationsCaseConcurrency}{scaled.Concurrency}{ReportWriterValues.ClosedLoopS1}");
        }
        else if (report.VectorProfile is { } vector)
        {
            text.AppendLine(CultureInfo.CurrentCulture, $"{ReportWriterValues.Run}{report.RunId}{ReportWriterValues.VectorProfile}{vector.Id}{ReportWriterValues.Seed}{vector.Seed}{ReportWriterValues.CorpusSHA256}{report.DatasetSha256}{ReportWriterValues.DigestCodeEnd}");
            text.AppendLine(CultureInfo.CurrentCulture, $"{ReportWriterValues.LineBreak}{vector.RecordCount}{ReportWriterValues.Vectors}{vector.Dimensions}{ReportWriterValues.Float32DimensionsCosineTop}{vector.TopK}{ReportWriterValues.ListSeparator}{vector.MeasuredQueries}{ReportWriterValues.MeasuredQueriesConcurrency}{vector.Concurrency}{ReportWriterValues.ClauseSeparator}{vector.IndexKind}{ReportWriterValues.AlgorithmModeSeparator}{vector.QueryMode}{ReportWriterValues.ClauseSeparator}{(vector.MinimumRecall).ToString(ReportWriterValues.P0Format, global::System.Globalization.CultureInfo.CurrentCulture)}{ReportWriterValues.MinimumRecall}");
        }
        else
        {
            throw new InvalidOperationException(ReportWriterValues.AComparisonReportMustCarryExactly);
        }
        text.AppendLine(CultureInfo.CurrentCulture, $"{ReportWriterValues.Host}{report.HostOs}{ReportWriterValues.ClauseSeparator}{report.Architecture}{ReportWriterValues.ClauseSeparator}{report.LogicalProcessors}{ReportWriterValues.LogicalProcessors}{report.Runtime}{ReportWriterValues.Load}{report.LoadModel}{ReportWriterValues.Storage}{report.Storage}{ReportWriterValues.SourceRevision}{report.SourceRevision ?? ReportWriterValues.Unrecorded}{ReportWriterValues.SentencePeriod}");
        text.AppendLine(ReportWriterValues.ExploratoryDevelopmentRunEngineTopologyResource);
        text.AppendLine(ReportWriterValues.EngineVersionTopologyWriteAcknowledgementRead);
        foreach (var target in report.Targets)
        {
            text.AppendLine(CultureInfo.CurrentCulture, $"{ReportWriterValues.MarkdownCellStart}{target.Name}{ReportWriterValues.MarkdownCellSeparator}{target.Version}{ReportWriterValues.MarkdownCellSeparator}{target.Topology}{ReportWriterValues.MarkdownCellSeparator}{target.WriteAcknowledgement}{ReportWriterValues.MarkdownCellSeparator}{target.ReadContract}{ReportWriterValues.ClauseSeparator}{target.Transport}{ReportWriterValues.ClauseSeparator}{target.Authorization}{ReportWriterValues.MarkdownCellSeparator}{target.Image ?? ReportWriterValues.SourceCheckout}{ReportWriterValues.MarkdownCellEnd}");
        }
        text.AppendLine(ReportWriterValues.CasesAreOrderedByVerifiedUseful);
        var latencyColumns = report.ScaledProfile is null ? ReportWriterValues.P50MsP95MsP99Ms : ReportWriterValues.P50SampleEstimateMsP95Sample;
        text.AppendLine(CultureInfo.CurrentCulture,
            $"{ReportWriterValues.ScenarioRunEngineStatusSuccessfulAttempts}{latencyColumns}{ReportWriterValues.UniqueCompleted}");
        foreach (var item in report.Cases.OrderBy(item => item.Scenario).ThenBy(item => item.Repetition).ThenByDescending(item => item.Measurement?.UsefulOperationsPerSecond ?? -ReportWriterValues.SingleElementOffset))
        {
            var m = item.Measurement;
            text.AppendLine(CultureInfo.CurrentCulture, $"{ReportWriterValues.MarkdownCellStart}{item.Scenario}{ReportWriterValues.MarkdownCellSeparator}{item.Repetition + ReportWriterValues.SingleElementOffset}{ReportWriterValues.MarkdownCellSeparator}{item.Target}{ReportWriterValues.MarkdownCellSeparator}{item.Status}{ReportWriterValues.MarkdownCellSeparator}{(m is null ? ReportWriterValues.UnavailableMeasurement : $"{m.Successes}{ReportWriterValues.PathSeparator}{m.Attempts}")}{ReportWriterValues.MarkdownCellSeparator}{Number(m?.UsefulOperationsPerSecond)}{ReportWriterValues.MarkdownCellSeparator}{Number(m?.Latency.P50Ms)}{ReportWriterValues.MarkdownCellSeparator}{Number(m?.Latency.P95Ms)}{ReportWriterValues.MarkdownCellSeparator}{Number(m?.Latency.P99Ms)}{ReportWriterValues.MarkdownCellSeparator}{m?.UniqueCompletedMessages.ToString(CultureInfo.InvariantCulture) ?? ReportWriterValues.UnavailableMeasurement}{ReportWriterValues.MarkdownCellEnd}");
        }
        AppendVectorTable(text, report);
        AppendFailureDetails(text, report);
        text.AppendLine(ReportWriterValues.QueueEnqueueReceiveACKPercentilesAnd);
        text.AppendLine(ReportWriterValues.ClientResourceMeasurementsCoverTheLoad);
        AppendClientResourceTable(text, report);
        text.AppendLine(ReportWriterValues.RemainingQualificationMatchedSynchronousRF3Baselines);
        return text.ToString();
    }

    private static void AppendVectorTable(StringBuilder text, ComparisonReport report)
    {
        if (report.VectorProfile is null)
        {
            return;
        }
        text.AppendLine(ReportWriterValues.EngineRecordsQueriesUsefulQueriesS);
        foreach (var item in report.Cases.Where(item => item.VectorMetrics is not null))
        {
            var vector = item.VectorMetrics!;
            text.AppendLine(CultureInfo.CurrentCulture, $"{ReportWriterValues.MarkdownCellStart}{item.Target}{ReportWriterValues.MarkdownCellSeparator}{vector.LoadedRecordCount}{ReportWriterValues.MarkdownCellSeparator}{vector.QuerySuccesses}{ReportWriterValues.MarkdownCellSeparator}{Number(vector.QueryUsefulOperationsPerSecond)}{ReportWriterValues.MarkdownCellSeparator}{Number(vector.LatencyP95Ms)}{ReportWriterValues.MarkdownCellSeparator}{Number(vector.LatencyP99Ms)}{ReportWriterValues.MarkdownCellSeparator}{Number(vector.ExactRecall)}{ReportWriterValues.MarkdownCellSeparator}{Number(vector.MinimumRecall)}{ReportWriterValues.MarkdownCellSeparator}{Number(vector.IndexBuildMilliseconds)}{ReportWriterValues.MarkdownCellEnd}");
        }
    }

    private static void AppendFailureDetails(StringBuilder text, ComparisonReport report)
    {
        foreach (var item in report.Cases.Where(item => item.Status == ComparisonStatuses.Failed && item.Detail is not null))
        {
            text.AppendLine(CultureInfo.CurrentCulture, $"{ReportWriterValues.LineBreak}{item.Target}{ReportWriterValues.PathSeparator}{item.Scenario}{ReportWriterValues.Repetition}{item.Repetition + ReportWriterValues.SingleElementOffset}{ReportWriterValues.LabelSeparator}{item.Detail}");
        }
    }

    private static void AppendClientResourceTable(StringBuilder text, ComparisonReport report)
    {
        text.AppendLine(ReportWriterValues.ScenarioRunEngineGeneratorCPUSeconds);
        foreach (var item in report.Cases.Where(item => item.Measurement?.ClientResources is not null))
        {
            var resource = item.Measurement!.ClientResources!;
            text.AppendLine(CultureInfo.CurrentCulture, $"{ReportWriterValues.MarkdownCellStart}{item.Scenario}{ReportWriterValues.MarkdownCellSeparator}{item.Repetition + ReportWriterValues.SingleElementOffset}{ReportWriterValues.MarkdownCellSeparator}{item.Target}{ReportWriterValues.MarkdownCellSeparator}{Number(resource.CpuSeconds)}{ReportWriterValues.MarkdownCellSeparator}{Number(resource.AllocatedBytes / ReportWriterValues.BytesPerMebibyte)}{ReportWriterValues.MarkdownCellSeparator}{Number(resource.PeakObservedWorkingSetBytes / ReportWriterValues.BytesPerMebibyte)}{ReportWriterValues.MarkdownCellEnd}");
        }
    }
    private static string Number(double? number) => number?.ToString(MetricFormat, CultureInfo.InvariantCulture) ?? ReportWriterValues.EmptyText;

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };
        ComparisonContractJson.Configure(options);
        return options;
    }
}
