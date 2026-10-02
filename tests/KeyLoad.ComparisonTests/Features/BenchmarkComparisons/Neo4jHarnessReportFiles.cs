using System.Text.Json;
using KeyLoad.Comparisons;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class Neo4jHarnessReportFiles : IAsyncDisposable
{
    private const string ReportDirectoryPrefix = "keyload-neo4j-harness-";
    private const string ResultsJsonFile = "results.json";
    private const string ResultsMarkdownFile = "results.md";
    private const string SamplesCsvFile = "samples.csv";
    private const string CleanupFailureMessage = "Neo4jHarnessReportCleanupFailed";

    public Neo4jHarnessReportFiles() => DirectoryPath = Path.Combine(Path.GetTempPath(), ReportDirectoryPrefix + Guid.NewGuid().ToString("N"));

    public string DirectoryPath { get; }

    public async Task VerifyAsync(ComparisonReport report, int expectedCsvLines, CancellationToken cancellationToken)
    {
        await ReportWriter.WriteAsync(report, DirectoryPath, cancellationToken);
        await VerifyJsonAsync(report, cancellationToken);
        await VerifyMarkdownAsync(report, cancellationToken);
        await VerifyCsvAsync(expectedCsvLines, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        await Neo4jHarnessFailureCollector.AttemptAsync(() =>
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }

            return Task.CompletedTask;
        }, failures);
        Neo4jHarnessFailureCollector.ThrowIfAny(CleanupFailureMessage, failures);
    }

    private async Task VerifyJsonAsync(ComparisonReport expected, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(Path.Combine(DirectoryPath, ResultsJsonFile), FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 65_536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var actual = await JsonSerializer.DeserializeAsync<ComparisonReport>(stream, ReportWriter.JsonOptions, cancellationToken);
        await Assert.That(actual is not null).IsTrue();
        await Assert.That(actual!.RunId == expected.RunId && actual.Options == expected.Options
            && actual.Targets.Length == expected.Targets.Length && actual.Cases.Length == expected.Cases.Length
            && actual.DatasetSha256 == expected.DatasetSha256).IsTrue();
    }

    private async Task VerifyMarkdownAsync(ComparisonReport report, CancellationToken cancellationToken)
    {
        var actual = await File.ReadAllTextAsync(Path.Combine(DirectoryPath, ResultsMarkdownFile), cancellationToken);
        await Assert.That(actual == ReportWriter.Markdown(report)).IsTrue();
    }

    private async Task VerifyCsvAsync(int expectedCsvLines, CancellationToken cancellationToken)
    {
        var lines = await File.ReadAllLinesAsync(Path.Combine(DirectoryPath, SamplesCsvFile), cancellationToken);
        await Assert.That(lines.Length == expectedCsvLines).IsTrue();
    }
}
