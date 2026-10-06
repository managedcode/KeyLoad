using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries;

internal static class TimeSeriesComparisonReportWriter
{
    private const string MarkdownText = "| {0} | {1} | {2} | {3} | {4} | {5} | {6} |";

    private static readonly CompositeFormat AttemptRowFormat = CompositeFormat.Parse(MarkdownText);

    private const string MillisecondsFormat = "F3";

    private const string JsonReportName = "timeseries-results.json";
    private const string MarkdownReportName = "timeseries-results.md";

    internal static async Task WriteAsync(TimeSeriesComparisonReport report, string outputDirectory, IOptions<NativeComparisonExecutionOptions> executionOptions)
    {
        var execution = NativeComparisonExecutionOptions.Require(executionOptions).Value;
        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(outputDirectory, JsonReportName);
        await using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None,
                         bufferSize: execution.ReportFileBufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            await JsonSerializer.SerializeAsync(stream, report, ReportWriter.JsonOptions);
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, MarkdownReportName), Markdown(report));
    }

    private static string Markdown(TimeSeriesComparisonReport report)
    {
        const string Token = "|---|---|---|---|---|---|";
        const string MarkdownToken = "| {0} | {1} | {2} | {3} | {4} | {5} |";
        const string TargetOperationAttemptMeasuredElapsedMsExpectedOutcomeErrorCodeContractText = "\n| Target | Operation | Attempt | Measured | Elapsed ms | Expected outcome | Error code |";
        const string MarkdownValueText = "|---|---|---:|---|---:|---|---|";
        const string MarkdownMarkdownValueText = "\nCorrectness checks compare exact canonical hashes with the independent deterministic oracle. Seed and readback setup are excluded from measured operations; the ManagedCode.TimeSeries arm is in-memory and has no persistence or recovery guarantee.";

        const string KeyLoadTimeSeriesComparisonToken = "# KeyLoad time-series comparison\n\n";
        const string SourceRevisionRunWorkloadSHA256Detail = "Source revision: `{0}`. Run: `{1}`. Workload SHA256: `{2}`.";
        const string SeparateStorageGuaranteesEvidenceNote = "{0} samples; bucket width {1}. Results are correctness evidence for separate storage guarantees, not a winner score.\n";
        const string TargetStoragePersistenceWriteAcknowledgementImagePackageContractText = "| Target | Storage | Persistence | Write acknowledgement | Image | Package |";
        const string UnavailableEvidenceMarker = "—";

        var text = new StringBuilder(KeyLoadTimeSeriesComparisonToken);
        text.AppendLine(string.Format(CultureInfo.InvariantCulture,
            SourceRevisionRunWorkloadSHA256Detail,
            report.SourceRevision, report.RunId, report.WorkloadHash));
        text.AppendLine(string.Format(CultureInfo.InvariantCulture,
            SeparateStorageGuaranteesEvidenceNote,
            report.SampleCount, report.BucketWidth));
        text.AppendLine(TargetStoragePersistenceWriteAcknowledgementImagePackageContractText);
        text.AppendLine(Token);
        foreach (var target in report.Targets)
        {
            text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                MarkdownToken, target.Name, target.StorageModel,
                target.PersistenceGuarantee, target.AcknowledgementGuarantee, target.Image ?? UnavailableEvidenceMarker,
                target.PackageVersion ?? UnavailableEvidenceMarker));
        }

        text.AppendLine(TargetOperationAttemptMeasuredElapsedMsExpectedOutcomeErrorCodeContractText);
        text.AppendLine(MarkdownValueText);
        foreach (var attempt in report.Attempts)
        {
            text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                AttemptRowFormat, attempt.Target, attempt.Operation,
                attempt.Attempt, attempt.Measured, attempt.ElapsedMilliseconds?.ToString(MillisecondsFormat, CultureInfo.InvariantCulture) ?? UnavailableEvidenceMarker,
                attempt.Succeeded, attempt.ErrorCode ?? UnavailableEvidenceMarker));
        }

        text.AppendLine(MarkdownMarkdownValueText);
        return text.ToString();
    }
}
