using System.Text.Json;
using System.Text.Json.Nodes;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Calls real production pure functions in Node without GitHub API or benchmark substitution.</summary>
internal static class SerializationGitHubStepNodeProcess
{
    private const string GitHubModule = "serialization-github.mjs";
    private const string ReadinessModule = "serialization-readiness.mjs";
    private const string ProbeSource = """
        import { readFileSync } from 'node:fs';
        import { pathToFileURL } from 'node:url';
        const input = JSON.parse(readFileSync(process.argv[1], 'utf8'));
        const github = await import(pathToFileURL(process.argv[2]).href);
        const readiness = await import(pathToFileURL(process.argv[3]).href);
        if (input.nonfinite) input.now = Number.NaN;
        const before = JSON.stringify(input);
        let accepted = false, ready = false, completed = false, wait = null, error = '', strictError = '';
        try {
          if (input.mode === 'deadline') wait = readiness.visibilityWait(input.start, input.now);
          else {
            ready = github.requiredStepsReady(input.job, input.measured);
            try { github.requireCompletedSteps(input.job, input.measured); completed = true; }
            catch (failure) { strictError = failure instanceof Error ? failure.message : 'invalid'; }
          }
          accepted = true;
        } catch (failure) { error = failure instanceof Error ? failure.message : 'invalid'; }
        process.stdout.write(JSON.stringify({accepted, ready, completed, wait, error, strictError,
          unchanged: before === JSON.stringify(input)}));
        """;

    internal static async Task<SerializationGitHubStepResponse> ProbeAsync(JsonObject input)
    {
        var root = Path.Combine(Path.GetTempPath(), "keyload-execution-serialization-steps-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "controlled-step-contract.json");
            await File.WriteAllTextAsync(path, input.ToJsonString(), TestContext.Current!.Execution.CancellationToken);
            var result = await SessionNodeProcess.RunAsync(
                ["--input-type=module", "-e", ProbeSource, path, SessionNodeProcess.Module(GitHubModule),
                    SessionNodeProcess.Module(ReadinessModule)], TestContext.Current!.Execution.CancellationToken);
            using var response = JsonDocument.Parse(result.Output);
            var value = response.RootElement;
            return new(result.ExitCode, result.Error, value.GetProperty(SerializationGitHubStepFields.Accepted).GetBoolean(),
                value.GetProperty(SerializationGitHubStepFields.Ready).GetBoolean(), value.GetProperty(SerializationGitHubStepFields.Completed).GetBoolean(),
                value.GetProperty(SerializationGitHubStepFields.Unchanged).GetBoolean(), value.GetProperty(SerializationGitHubStepFields.Error).GetString()!,
                value.GetProperty(SerializationGitHubStepFields.StrictError).GetString()!,
                value.GetProperty(SerializationGitHubStepFields.Wait).ValueKind == JsonValueKind.Null
                    ? null : value.GetProperty(SerializationGitHubStepFields.Wait).GetDouble());
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
