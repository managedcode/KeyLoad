using System.Text.Json;
using System.Text.Json.Nodes;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>Invokes the production validator on controlled data without running benchmarks.</summary>
internal static class NativeSerializationReportNodeProcess
{
    private const string ReportModule = "native-serialization-report.mjs";
    private const string CorpusModule = "native-serialization-corpus.mjs";
    private const string ProbeSource = $$"""
        import { readFileSync } from 'node:fs';
        import { pathToFileURL } from 'node:url';
        const modeKey = '{{NativeSerializationReportFields.Mode}}';
        const nonfiniteKey = '{{NativeSerializationReportFields.Nonfinite}}';
        const reportsKey = '{{NativeSerializationReportFields.Reports}}';
        const benchmarksKey = '{{NativeSerializationReportFields.Benchmarks}}';
        const statisticsKey = '{{NativeSerializationReportFields.Statistics}}';
        const meanKey = '{{NativeSerializationReportFields.Mean}}';
        const corpusKey = '{{NativeSerializationReportFields.Corpus}}';
        const acceptedKey = '{{NativeSerializationReportFields.Accepted}}';
        const errorKey = '{{NativeSerializationReportFields.Error}}';
        const unchangedKey = '{{NativeSerializationReportFields.Unchanged}}';
        const input = JSON.parse(readFileSync(process.argv[1], 'utf8'));
        const reports = await import(pathToFileURL(process.argv[2]).href);
        const corpus = await import(pathToFileURL(process.argv[3]).href);
        if (input[nonfiniteKey]) input[reportsKey][0][benchmarksKey][0][statisticsKey][meanKey] = Number.NaN;
        const before = JSON.stringify(input);
        let accepted = false, error = '';
        try {
          if (input[modeKey] === 'corpus') corpus.validateCorpusManifest(input[corpusKey],
            'NativeDocumentSerializationBenchmarks', 1024);
          else if (reports.validateNativeSerializationReports(input[reportsKey]).cellCount !== 24)
            throw new Error('validator omitted a required cell');
          accepted = true;
        } catch (failure) { error = failure instanceof Error ? failure.message : 'invalid'; }
        process.stdout.write(JSON.stringify({[acceptedKey]: accepted, [errorKey]: error, [unchangedKey]: before === JSON.stringify(input)}));
        """;

    internal static async Task<NativeSerializationReportResponse> ProbeAsync(JsonNode data, bool corpus = false,
        bool nonfinite = false)
    {
        var root = Path.Combine(Path.GetTempPath(), "keyload-native-serialization-report-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "controlled-schema.json");
            var input = new JsonObject
            {
                [corpus ? "corpus" : "reports"] = data,
                [NativeSerializationReportFields.Mode] = corpus ? "corpus" : "reports",
                [NativeSerializationReportFields.Nonfinite] = nonfinite
            };
            await File.WriteAllTextAsync(path, input.ToJsonString(), TestContext.Current!.Execution.CancellationToken);
            var result = await IsolatedAggregateNodeProcess.RunAsync(
                ["--input-type=module", "-e", ProbeSource, path, IsolatedAggregateNodeProcess.Module(ReportModule),
                    IsolatedAggregateNodeProcess.Module(CorpusModule)], TestContext.Current!.Execution.CancellationToken);
            using var response = JsonDocument.Parse(result.Output);
            return new(result.ExitCode, result.Error, response.RootElement.GetProperty(NativeSerializationReportFields.Accepted).GetBoolean(),
                response.RootElement.GetProperty(NativeSerializationReportFields.Unchanged).GetBoolean(), response.RootElement.GetProperty(NativeSerializationReportFields.Error).GetString()!);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}

internal sealed record NativeSerializationReportResponse(int ExitCode, string StandardError,
    bool Accepted, bool Unchanged, string Error);
