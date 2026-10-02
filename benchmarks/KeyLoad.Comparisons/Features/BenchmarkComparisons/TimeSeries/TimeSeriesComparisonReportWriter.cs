using System.Globalization;
using System.Text;
using System.Text.Json;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries;

internal static class TimeSeriesComparisonReportWriter
{
    private const string JsonReportName = "timeseries-results.json";
    private const string MarkdownReportName = "timeseries-results.md";

    internal static async Task WriteAsync(TimeSeriesComparisonReport report, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(outputDirectory, JsonReportName);
        await using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None,
                         bufferSize: 65_536, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            await JsonSerializer.SerializeAsync(stream, report, ReportWriter.JsonOptions);
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, MarkdownReportName), Markdown(report));
    }

    private static string Markdown(TimeSeriesComparisonReport report)
    {
        var text = new StringBuilder("# KeyLoad time-series comparison\n\n");
        text.AppendLine(string.Format(CultureInfo.InvariantCulture,
            "Source revision: `{0}`. Run: `{1}`. Workload SHA256: `{2}`.",
            report.SourceRevision, report.RunId, report.WorkloadHash));
        text.AppendLine(string.Format(CultureInfo.InvariantCulture,
            "{0} samples; bucket width {1}. Results are correctness evidence for separate storage guarantees, not a winner score.\n",
            report.SampleCount, report.BucketWidth));
        text.AppendLine("| Target | Storage | Persistence | Write acknowledgement | Image | Package |");
        text.AppendLine("|---|---|---|---|---|---|");
        foreach (var target in report.Targets)
        {
            text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "| {0} | {1} | {2} | {3} | {4} | {5} |", target.Name, target.StorageModel,
                target.PersistenceGuarantee, target.AcknowledgementGuarantee, target.Image ?? "—",
                target.PackageVersion ?? "—"));
        }

        text.AppendLine("\n| Target | Operation | Attempt | Measured | Elapsed ms | Expected outcome | Error code |");
        text.AppendLine("|---|---|---:|---|---:|---|---|");
        foreach (var attempt in report.Attempts)
        {
            text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "| {0} | {1} | {2} | {3} | {4} | {5} | {6} |", attempt.Target, attempt.Operation,
                attempt.Attempt, attempt.Measured, attempt.ElapsedMilliseconds?.ToString("F3", CultureInfo.InvariantCulture) ?? "—",
                attempt.Succeeded, attempt.ErrorCode ?? "—"));
        }

        text.AppendLine("\nCorrectness checks compare exact canonical hashes with the independent deterministic oracle. Seed and readback setup are excluded from measured operations; the ManagedCode.TimeSeries arm is in-memory and has no persistence or recovery guarantee.");
        return text.ToString();
    }
}
