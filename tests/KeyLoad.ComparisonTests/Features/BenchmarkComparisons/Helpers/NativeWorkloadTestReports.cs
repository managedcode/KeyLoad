namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class NativeWorkloadTestReports
{
    private const string ReportsDirectory = "TestResults";
    private const string ReportPattern = "*.trx";
    private const int MaximumReports = 1;
    private const long MaximumReportBytes = 8 * 1024 * 1024;
    private const string InvalidReport = "The native workload TRX report exceeded its retention bounds.";

    internal static void Retain(string output, string evidence)
    {
        var source = Path.Combine(output, ReportsDirectory);
        if (!Directory.Exists(source))
        { return; }
        var reports = Directory.EnumerateFiles(source, ReportPattern).Take(MaximumReports + 1).ToArray();
        if (reports.Length > MaximumReports)
        { throw new InvalidDataException(InvalidReport); }
        var destination = Path.Combine(Path.GetDirectoryName(ComparisonProgressLine.PathForEvidenceDirectory(evidence))!, ReportsDirectory);
        foreach (var report in reports)
        {
            var file = new FileInfo(report);
            if (file.Length > MaximumReportBytes || file.LinkTarget is not null)
            { throw new InvalidDataException(InvalidReport); }
            Directory.CreateDirectory(destination);
            File.Copy(report, Path.Combine(destination, file.Name), overwrite: false);
        }
    }
}
