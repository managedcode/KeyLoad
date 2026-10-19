using System.Text.Json;
using System.Text.Json.Nodes;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Invokes the production validator on controlled data without running benchmarks.</summary>
internal static class SerializationReportNodeProcess
{
    private const string ReportModule = "serialization-report.mjs";
    private const string CorpusModule = "serialization-corpus.mjs";
    private const string ProbeSource = $$"""
        import { readFileSync } from 'node:fs';
        import { pathToFileURL } from 'node:url';
        const modeKey = '{{SerializationReportFields.Mode}}';
        const nonfiniteKey = '{{SerializationReportFields.Nonfinite}}';
        const reportsKey = '{{SerializationReportFields.Reports}}';
        const benchmarksKey = '{{SerializationReportFields.Benchmarks}}';
        const statisticsKey = '{{SerializationReportFields.Statistics}}';
        const meanKey = '{{SerializationReportFields.Mean}}';
        const corpusKey = '{{SerializationReportFields.Corpus}}';
        const acceptedKey = '{{SerializationReportFields.Accepted}}';
        const errorKey = '{{SerializationReportFields.Error}}';
        const unchangedKey = '{{SerializationReportFields.Unchanged}}';
        const input = JSON.parse(readFileSync(process.argv[1], 'utf8'));
        const reports = await import(pathToFileURL(process.argv[2]).href);
        const corpus = await import(pathToFileURL(process.argv[3]).href);
        if (input[nonfiniteKey]) input[reportsKey][0][benchmarksKey][0][statisticsKey][meanKey] = Number.NaN;
        const before = JSON.stringify(input);
        let accepted = false, error = '';
        try {
          if (input[modeKey] === 'corpus') corpus.validateCorpusManifest(input[corpusKey],
            'DocumentSerializationBenchmarks', 1024);
          else if (reports.validateSerializationReports(input[reportsKey]).cellCount !== 24)
            throw new Error('validator omitted a required cell');
          accepted = true;
        } catch (failure) { error = failure instanceof Error ? failure.message : 'invalid'; }
        process.stdout.write(JSON.stringify({[acceptedKey]: accepted, [errorKey]: error, [unchangedKey]: before === JSON.stringify(input)}));
        """;

    internal static async Task<SerializationReportResponse> ProbeAsync(JsonNode data, bool corpus = false,
        bool nonfinite = false)
    {
        var root = Path.Combine(Path.GetTempPath(), "keyload-execution-serialization-report-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "controlled-schema.json");
            var input = new JsonObject
            {
                [corpus ? "corpus" : "reports"] = data,
                [SerializationReportFields.Mode] = corpus ? "corpus" : "reports",
                [SerializationReportFields.Nonfinite] = nonfinite
            };
            await File.WriteAllTextAsync(path, input.ToJsonString(), TestContext.Current!.Execution.CancellationToken);
            var result = await SessionNodeProcess.RunAsync(
                ["--input-type=module", "-e", ProbeSource, path, SessionNodeProcess.Module(ReportModule),
                    SessionNodeProcess.Module(CorpusModule)], TestContext.Current!.Execution.CancellationToken);
            using var response = JsonDocument.Parse(result.Output);
            return new(result.ExitCode, result.Error, response.RootElement.GetProperty(SerializationReportFields.Accepted).GetBoolean(),
                response.RootElement.GetProperty(SerializationReportFields.Unchanged).GetBoolean(), response.RootElement.GetProperty(SerializationReportFields.Error).GetString()!);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
