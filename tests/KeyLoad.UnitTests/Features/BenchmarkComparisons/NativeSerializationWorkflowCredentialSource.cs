using KeyLoad.UnitTests.Features.RepositoryGovernance;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>Checks real workflow scopes and rejects unsupported environment-map syntax.</summary>
internal static class NativeSerializationWorkflowCredentialSource
{
    internal const string Workflow = "benchmarks.yml";
    internal const string Job = "native-serialization";
    internal const string Prepare = "Record source packages runtime and execution identity";
    internal const string Verify = "Require all original measurements and their provenance";
    internal const string Measurement = "Measure all native and historical JSON diagnostic cases";
    internal const string Token = "${{ github.token }}";
    private const string EvidenceCommand = "node scripts/Features/BenchmarkComparisons/native-serialization-evidence.mjs";
    private const string CorpusVariable = "KEYLOAD_NATIVE_SERIALIZATION_CORPUS_DIRECTORY";
    private const string CorpusDirectory = "${{ github.workspace }}/artifacts/native-serialization/corpus";

    internal static int Validate(string workflow)
    {
        var jobs = workflow.IndexOf("\njobs:\n", StringComparison.Ordinal);
        Require(jobs >= 0, "jobs boundary");
        Require(NoCredentials(EnvironmentBindings(workflow[..jobs], 0)), "workflow environment");
        var job = WorkflowLayoutSource.JobBlock(workflow, Job);
        var steps = job.IndexOf("    steps:\n", StringComparison.Ordinal);
        Require(steps >= 0, "native steps boundary");
        Require(NoCredentials(EnvironmentBindings(job[..steps], 4)), "native job environment");
        var preparing = 0;
        var verifying = 0;
        foreach (var step in WorkflowStepNameTests.StepBlocks(job))
        {
            var name = StepName(step);
            var environment = EnvironmentBindings(step, 8);
            var credentials = environment.Where(IsCredential).ToArray();
            var mode = name == Prepare ? "prepare" : name == Verify ? "verify" : null;
            if (mode is null)
            {
                Require(credentials.Length == 0, "unauthenticated native step environment");
                continue;
            }

            Require(credentials.Length == 1 && credentials[0].Key == "GH_TOKEN"
                && credentials[0].Value == Token, "ephemeral step credential");
            Require(Scalar(step, "        run:") == EvidenceCommand + " --mode=" + mode
                + " --directory=artifacts/native-serialization", "authenticated evidence command");
            Require(environment.Any(static binding => binding.Key == CorpusVariable
                && binding.Value == CorpusDirectory), "authenticated corpus directory");
            preparing += mode == "prepare" ? 1 : 0;
            verifying += mode == "verify" ? 1 : 0;
        }

        Require(preparing == 1 && verifying == 1, "unique prepare and verify steps");
        return preparing + verifying;
    }

    internal static string Step(string workflow, string name)
        => WorkflowStepNameTests.StepBlocks(WorkflowLayoutSource.JobBlock(workflow, Job))
            .Single(step => StepName(step) == name);

    private static KeyValuePair<string, string>[] EnvironmentBindings(string source, int scopeIndent)
    {
        var prefix = new string(' ', scopeIndent) + "env:";
        var lines = source.Split('\n').Select(static line => line.TrimEnd('\r')).ToArray();
        RequireLiteralScopeKeys(lines, scopeIndent);
        var headings = Enumerable.Range(0, lines.Length)
            .Where(index => lines[index].StartsWith(prefix, StringComparison.Ordinal)).ToArray();
        Require(headings.Length <= 1, "duplicate environment map");
        if (headings.Length == 0)
        {
            return [];
        }

        Require(lines[headings[0]] == prefix, "literal environment map");
        var bindings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in lines.Skip(headings[0] + 1))
        {
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            var indent = line.TakeWhile(char.IsWhiteSpace).Count();
            if (indent <= scopeIndent)
            {
                break;
            }

            Require(indent == scopeIndent + 2, "environment binding indentation");
            var entry = line[indent..];
            var colon = entry.IndexOf(':', StringComparison.Ordinal);
            Require(colon > 0, "environment binding key");
            var key = entry[..colon];
            var value = entry[(colon + 1)..].Trim();
            Require(key.All(static character => char.IsAsciiLetterOrDigit(character) || character == '_')
                && value.Length > 0 && !"&*|>{[".Contains(value[0], StringComparison.Ordinal), "literal environment binding");
            Require(bindings.TryAdd(key, value), "duplicate environment binding");
        }

        return [.. bindings];
    }

    private static void RequireLiteralScopeKeys(IEnumerable<string> lines, int scopeIndent)
    {
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            var indent = line.TakeWhile(char.IsWhiteSpace).Count();
            if (indent != scopeIndent)
            {
                continue;
            }

            var entry = line[indent..];
            var colon = entry.IndexOf(':', StringComparison.Ordinal);
            Require(colon > 0, "literal scope mapping");
            var key = entry[..colon];
            Require((char.IsAsciiLetter(key[0]) || key[0] == '_')
                && key.All(static character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-'),
                "literal scope mapping key");
        }
    }

    private static string StepName(string step)
    {
        var lines = step.Split('\n').Where(static line => line.StartsWith("      - name:", StringComparison.Ordinal)
            || line.StartsWith("        name:", StringComparison.Ordinal)).ToArray();
        Require(lines.Length == 1, "unique native step name");
        return lines[0][(lines[0].IndexOf(':', StringComparison.Ordinal) + 1)..].Trim();
    }

    private static string Scalar(string source, string prefix)
    {
        var lines = source.Split('\n').Where(line => line.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
        Require(lines.Length == 1, "unique native step property");
        return lines[0][prefix.Length..].Trim();
    }

    private static bool NoCredentials(IEnumerable<KeyValuePair<string, string>> bindings)
        => !bindings.Any(IsCredential);

    private static bool IsCredential(KeyValuePair<string, string> binding)
        => binding.Key is "GH_TOKEN" or "GITHUB_TOKEN" || SensitiveExpression(binding.Value);

    private static bool SensitiveExpression(string value)
    {
        var expression = new string(value.Where(static character => !char.IsWhiteSpace(character)).ToArray());
        return expression.Contains("github.token", StringComparison.OrdinalIgnoreCase)
            || expression.Contains("github['token']", StringComparison.OrdinalIgnoreCase)
            || expression.Contains("github[\"token\"]", StringComparison.OrdinalIgnoreCase)
            || expression.Contains("secrets", StringComparison.OrdinalIgnoreCase);
    }

    private static void Require(bool valid, string boundary)
    {
        if (!valid)
        {
            throw new InvalidDataException("Native workflow credential boundary is invalid: " + boundary);
        }
    }
}
