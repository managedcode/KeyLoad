using System.Text.Json;
using System.Text.Json.Nodes;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>Calls real production pure functions in Node without GitHub API or benchmark substitution.</summary>
internal static class NativeSerializationGitHubStepNodeProcess
{
    private const string GitHubModule = "native-serialization-github.mjs";
    private const string ReadinessModule = "native-serialization-readiness.mjs";
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

    internal static async Task<NativeSerializationGitHubStepResponse> ProbeAsync(JsonObject input)
    {
        var root = Path.Combine(Path.GetTempPath(), "keyload-native-serialization-steps-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "controlled-step-contract.json");
            await File.WriteAllTextAsync(path, input.ToJsonString(), TestContext.Current!.Execution.CancellationToken);
            var result = await IsolatedAggregateNodeProcess.RunAsync(
                ["--input-type=module", "-e", ProbeSource, path, IsolatedAggregateNodeProcess.Module(GitHubModule),
                    IsolatedAggregateNodeProcess.Module(ReadinessModule)], TestContext.Current!.Execution.CancellationToken);
            using var response = JsonDocument.Parse(result.Output);
            var value = response.RootElement;
            return new(result.ExitCode, result.Error, value.GetProperty(NativeSerializationGitHubStepFields.Accepted).GetBoolean(),
                value.GetProperty(NativeSerializationGitHubStepFields.Ready).GetBoolean(), value.GetProperty(NativeSerializationGitHubStepFields.Completed).GetBoolean(),
                value.GetProperty(NativeSerializationGitHubStepFields.Unchanged).GetBoolean(), value.GetProperty(NativeSerializationGitHubStepFields.Error).GetString()!,
                value.GetProperty(NativeSerializationGitHubStepFields.StrictError).GetString()!,
                value.GetProperty(NativeSerializationGitHubStepFields.Wait).ValueKind == JsonValueKind.Null
                    ? null : value.GetProperty(NativeSerializationGitHubStepFields.Wait).GetDouble());
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}

internal sealed record NativeSerializationGitHubStepResponse(int ExitCode, string StandardError,
    bool Accepted, bool Ready, bool Completed, bool Unchanged, string Error, string StrictError, double? Wait);
