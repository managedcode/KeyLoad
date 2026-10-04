using System.Text.Json;
using System.Text.Json.Nodes;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class RawStorageReportNodeProcess
{
    private const string ModuleMode = "--input-type=module";
    private const string EvaluationMode = "-e";
    private const int MaximumRequestBytes = 64 * 1024;
    private const string OversizedRequestMessage = "The controlled validator request exceeds 64 KiB.";
    private const string ValidatorModule = "raw-storage-report.mjs";
    private const string ResultsField = "results";
    private const string NameField = "name";
    private const string AcceptedField = "accepted";
    private const string UnchangedField = "unchanged";
    private const string ErrorField = "error";
    private const string ProbeSource = """
        import { pathToFileURL } from 'node:url';
        const input = JSON.parse(Buffer.from(process.argv[1], 'base64').toString('utf8'));
        const validator = await import(pathToFileURL(process.argv[2]).href);
        const parameters = (benchmark, field, value) => {
          const pairs = benchmark.Parameters.split('&').map((pair) => pair.split('='));
          const target = pairs.find((pair) => pair[0] === field);
          if (!target) throw new Error('controlled mutation target missing');
          target[1] = value;
          benchmark.Parameters = pairs.map((pair) => pair.join('=')).join('&');
        };
        const resultRows = (benchmark) => benchmark.Measurements.filter(
          (row) => row.IterationMode === 'Workload' && row.IterationStage === 'Result');
        const actualRows = (benchmark) => benchmark.Measurements.filter(
          (row) => row.IterationMode === 'Workload' && row.IterationStage === 'Actual');
        function mutateMeasurementScenario(name, first) {
          switch (name) {
            case 'nonfinite-nanoseconds': resultRows(first)[0].Nanoseconds = Number.POSITIVE_INFINITY; break;
            case 'nonfinite-actual-nanoseconds': actualRows(first)[0].Nanoseconds = Number.POSITIVE_INFINITY; break;
            case 'zero-result-operations': resultRows(first)[0].Operations = 0; break;
            case 'incomplete-actual-rows': first.Measurements = first.Measurements.filter(
              (row) => !(row.IterationMode === 'Workload' && row.IterationStage === 'Actual'
                && row.IterationIndex === actualRows(first)[4].IterationIndex)); break;
            case 'duplicate-actual-iteration': actualRows(first)[1].IterationIndex = actualRows(first)[0].IterationIndex; break;
            case 'retained-n-mismatch': first.Statistics.N = 2; first.Statistics.OriginalValues.pop(); break;
            case 'cross-stage-launch': resultRows(first).forEach((row) => { row.LaunchIndex = 1; }); break;
            case 'missing-memory': delete first.Memory; break;
            case 'negative-allocation': first.Memory.BytesAllocatedPerOperation = -1; break;
            case 'zero-memory-operations': first.Memory.TotalOperations = 0; break;
            case 'four-result-rows': first.Measurements.push(
              {...structuredClone(resultRows(first)[0]), IterationIndex: 4}); break;
            case 'six-result-rows':
              for (let iteration = 4; iteration <= 6; iteration++) {
                first.Measurements.push({...structuredClone(resultRows(first)[0]), IterationIndex: iteration});
              }
              break;
            case 'duplicate-iteration': resultRows(first)[1].IterationIndex = resultRows(first)[0].IterationIndex; break;
            case 'multiple-launches': resultRows(first)[1].LaunchIndex = 1; break;
            default: return false;
          }
          return true;
        }
        function applyScenario(name, input) {
          const report = structuredClone(input.report);
          let expectedEngine = input.engine;
          const first = report.Benchmarks[0];
          if (mutateMeasurementScenario(name, first)) return {report, expectedEngine};
          switch (name) {
            case 'valid': break;
            case 'missing-entry': report.Benchmarks.pop(); break;
            case 'extra-entry': report.Benchmarks.push({...structuredClone(first), Method: 'Extra'}); break;
            case 'duplicate-entry': report.Benchmarks[report.Benchmarks.length - 1] = structuredClone(first); break;
            case 'wrong-engine': parameters(first, 'Engine', 'tsavorite'); break;
            case 'wrong-payload': parameters(first, 'PayloadBytes', '64'); break;
            case 'wrong-count': parameters(first, 'RecordCount', '4095'); break;
            case 'null-statistics': first.Statistics = null; break;
            case 'bad-n-zero': first.Statistics.N = 0; first.Statistics.OriginalValues = []; break;
            case 'bad-n-high': first.Statistics.N = 6; first.Statistics.OriginalValues.push(105, 106, 107); break;
            case 'bad-original-count': first.Statistics.OriginalValues.pop(); break;
            case 'nonfinite-mean': first.Statistics.Mean = Number.NaN; break;
            case 'wrong-version': report.HostEnvironmentInfo.BenchmarkDotNetVersion = '0.15.7'; break;
            case 'wrong-runtime': report.HostEnvironmentInfo.RuntimeVersion = '.NET 9.0.0'; break;
            case 'wrong-runtime-major-boundary': report.HostEnvironmentInfo.RuntimeVersion = '.NET 100.0.0'; break;
            case 'malformed-runtime-version': report.HostEnvironmentInfo.RuntimeVersion = '.NET 10bogus'; break;
            case 'wrong-namespace': first.Namespace = 'untrusted-namespace'; break;
            case 'wrong-type': first.Type = 'OtherBenchmarks'; break;
            case 'wrong-method': first.Method = 'OtherMethod'; break;
            case 'duplicate-parameter': first.Parameters = first.Parameters.replace('PayloadBytes=', 'Engine='); break;
            case 'extra-parameter': first.Parameters += '&Unexpected=value'; break;
            case 'sentinel-error': parameters(first, 'Engine', 'SENSITIVE_SENTINEL'); break;
            case 'wrong-engine-argument': expectedEngine = 'ZoneTree'; break;
            default: throw new Error('unknown controlled mutation');
          }
          return {report, expectedEngine};
        }
        const scenarios = input.scenarios.map((name) => {
          const {report, expectedEngine} = applyScenario(name, input);
          const beforeValidation = JSON.stringify(report);
          try {
            const result = validator.validateReport(report, expectedEngine);
            const unchanged = JSON.stringify(report) === beforeValidation;
            return {name, accepted: result === undefined && unchanged, unchanged,
              error: result === undefined ? '' : 'unexpected return value'};
          } catch (error) {
            return {name, accepted: false, unchanged: JSON.stringify(report) === beforeValidation,
              error: error instanceof Error ? error.message : 'invalid report'};
          }
        });
        process.stdout.write(JSON.stringify({results: scenarios}));
        """;

    internal static async Task<RawStorageReportProbeResponse> ValidateAsync(JsonObject report, string engine,
        IReadOnlyList<string> scenarios, CancellationToken cancellationToken)
    {
        var request = JsonSerializer.SerializeToUtf8Bytes(new
        {
            report,
            engine,
            scenarios
        });
        if (request.Length > MaximumRequestBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(report), OversizedRequestMessage);
        }

        var encodedRequest = Convert.ToBase64String(request);
        var response = await IsolatedAggregateNodeProcess.RunAsync(
            [ModuleMode, EvaluationMode, ProbeSource, encodedRequest,
                IsolatedAggregateNodeProcess.Module(ValidatorModule)], cancellationToken);
        return ParseResponse(response);
    }

    private static RawStorageReportProbeResponse ParseResponse(IsolatedAggregateNodeResult response)
    {
        using var document = JsonDocument.Parse(response.Output);
        var cases = document.RootElement.GetProperty(ResultsField).EnumerateArray()
            .Select(result => new RawStorageReportCaseResult(
                result.GetProperty(NameField).GetString()!,
                result.GetProperty(AcceptedField).GetBoolean(),
                result.GetProperty(UnchangedField).GetBoolean(),
                result.GetProperty(ErrorField).GetString()!))
            .ToArray();
        return new(response.ExitCode, response.Error, cases);
    }
}

internal sealed record RawStorageReportProbeResponse(int ExitCode, string StandardError,
    RawStorageReportCaseResult[] Cases);

internal sealed record RawStorageReportCaseResult(string Name, bool Accepted, bool Unchanged, string Error);
