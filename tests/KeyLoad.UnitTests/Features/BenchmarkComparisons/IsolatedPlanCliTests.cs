using System.Text.Json.Nodes;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>AC-ISO-002/007: the actual CLI writes complete plans without replacing caller files.</summary>
internal sealed class IsolatedPlanCliTests
{
    [Test]
    public async Task AcIso002CliRetainsFullPlanAndEmitsBothCompleteGithubMatrices()
    {
        using var directory = new IsolatedPlanDirectory();
        var planPath = directory.PathFor("case plan.json");
        var githubPath = directory.PathFor("github output.txt");
        var token = TestContext.Current!.Execution.CancellationToken;
        await File.WriteAllTextAsync(githubPath, "sentinel=preserved\n", token);
        var result = await IsolatedPlanNodeProcess.CliAsync($"--output={planPath}", $"--github-output={githubPath}");
        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.Error);
        await Assert.That(result.Error).IsEmpty();
        var plan = JsonNode.Parse(result.Output)!;
        var retained = JsonNode.Parse(await File.ReadAllTextAsync(planPath, token))!;
        await Assert.That(JsonNode.DeepEquals(plan, retained)).IsTrue();
        var canonical = (await IsolatedPlanNodeProcess.ProbeAsync("create"))[IsolatedPlanFields.Value]!;
        await Assert.That(JsonNode.DeepEquals(plan, canonical)).IsTrue();
        var lines = await File.ReadAllLinesAsync(githubPath, token);
        await Assert.That(lines.Length).IsEqualTo(4);
        await Assert.That(lines[0]).IsEqualTo("sentinel=preserved");
        await VerifyMatrixAsync(lines[1], "crud_matrix=", plan[IsolatedPlanFields.Matrices]![IsolatedPlanFields.Crud]!, 108);
        await VerifyMatrixAsync(lines[2], "specialized_matrix=", plan[IsolatedPlanFields.Matrices]![IsolatedPlanFields.Specialized]!, 162);
        var preflight = (await IsolatedPlanNodeProcess.ProbeAsync("preflight", plan))[IsolatedPlanFields.Value]!;
        await VerifyMatrixAsync(lines[3], "preflight_matrix=", preflight, 27);
    }

    [Test]
    public async Task AcIso007CliNeverOverwritesExistingPlanOrAppendsOnRefusal()
    {
        using var directory = new IsolatedPlanDirectory();
        var output = directory.PathFor("existing.json");
        var github = directory.PathFor("github.txt");
        var token = TestContext.Current!.Execution.CancellationToken;
        await File.WriteAllTextAsync(output, "owned-plan-bytes", token);
        await File.WriteAllTextAsync(github, "owned-github-bytes", token);
        var result = await IsolatedPlanNodeProcess.CliAsync($"--output={output}", $"--github-output={github}");
        await AssertFailureAsync(result);
        await Assert.That(await File.ReadAllTextAsync(output, token)).IsEqualTo("owned-plan-bytes");
        await Assert.That(await File.ReadAllTextAsync(github, token)).IsEqualTo("owned-github-bytes");
    }

    [Test]
    [Arguments("unknown")]
    [Arguments("duplicate-output")]
    [Arguments("duplicate-github")]
    [Arguments("empty-output")]
    [Arguments("missing-github")]
    [Arguments("directory-github")]
    public async Task AcIso007InvalidCliArgumentsFailBeforeCreatingPlan(string error)
    {
        using var directory = new IsolatedPlanDirectory();
        var output = directory.PathFor("new.json");
        var github = directory.PathFor("missing-github.txt");
        var arguments = InvalidArguments(error, output, github, directory.Root);
        var result = await IsolatedPlanNodeProcess.CliAsync(arguments);
        await AssertFailureAsync(result);
        await Assert.That(File.Exists(output)).IsFalse();
        await Assert.That(File.Exists(github)).IsFalse();
    }

    private static string[] InvalidArguments(string error, string output, string github, string root) => error switch
    {
        "unknown" => [$"--output={output}", "--secret=must-not-leak"],
        "duplicate-output" => [$"--output={output}", $"--output={output}"],
        "duplicate-github" => [$"--output={output}", $"--github-output={github}", $"--github-output={github}"],
        "empty-output" => ["--output="],
        "missing-github" => [$"--output={output}", $"--github-output={github}"],
        "directory-github" => [$"--output={output}", $"--github-output={root}"],
        _ => throw new ArgumentOutOfRangeException(nameof(error))
    };

    private static async Task AssertFailureAsync(IsolatedPlanProcessResult result)
    {
        await Assert.That(result.ExitCode).IsEqualTo(1);
        await Assert.That(result.Output).IsEmpty();
        await Assert.That(result.Error.Trim()).IsEqualTo("Isolated comparison planning failed.");
    }

    private static async Task VerifyMatrixAsync(string line, string prefix, JsonNode expected, int count)
    {
        await Assert.That(line.StartsWith(prefix, StringComparison.Ordinal)).IsTrue();
        var matrix = JsonNode.Parse(line[prefix.Length..])!;
        await Assert.That(JsonNode.DeepEquals(matrix, expected)).IsTrue();
        await Assert.That(matrix[IsolatedPlanFields.Include]!.AsArray().Count).IsEqualTo(count);
    }
}
