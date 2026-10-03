using System.Text.Json;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class IsolatedAggregateProbe
{
    private const string InputMode = "--input-type=module";
    private const string EvalMode = "-e";
    private const string RequestFile = "request.json";
    private const string Prefix = "keyload-isolated-contract-";
    private const string ValidatorModule = "aggregate-validation.mjs";
    private const string ProbeSource = """
        import { readFileSync } from 'node:fs';
        import { pathToFileURL } from 'node:url';
        const request = JSON.parse(readFileSync(process.argv[1], 'utf8'));
        const validator = await import(pathToFileURL(process.argv[2]).href);
        try {
          const value = validator.validateWorkerEnvelope(request.value, request.cell, request.cohort, request.contract);
          process.stdout.write(JSON.stringify({accepted:true,schemaVersion:value.schemaVersion})+'\n');
        } catch {
          process.stdout.write(JSON.stringify({accepted:false})+'\n');
        }
        """;

    internal static async Task<bool> AcceptedAsync(object value, object cell, CancellationToken token)
    {
        var root = Path.Combine(Path.GetTempPath(), Prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var file = Path.Combine(root, RequestFile);
            var request = new { value, cell, cohort = IsolatedAggregateData.Cohort(), contract = IsolatedAggregateData.Contract() };
            await File.WriteAllBytesAsync(file, JsonSerializer.SerializeToUtf8Bytes(request, IsolatedAggregateData.JsonOptions), token);
            var response = await IsolatedAggregateNodeProcess.RunAsync(
                [InputMode, EvalMode, ProbeSource, file, IsolatedAggregateNodeProcess.Module(ValidatorModule)], token);
            await Assert.That(response.ExitCode).IsEqualTo(0);
            await Assert.That(response.Error).IsEqualTo(string.Empty);
            using var result = JsonDocument.Parse(response.Output);
            return result.RootElement.GetProperty(IsolatedAggregateFields.Accepted).GetBoolean();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
