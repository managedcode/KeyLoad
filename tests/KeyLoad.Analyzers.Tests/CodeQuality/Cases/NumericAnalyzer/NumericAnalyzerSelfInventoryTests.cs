using Microsoft.CodeAnalysis;

namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

/// <summary>AC-CQ-008: analyzes both centrally excluded infrastructure projects via Roslyn.</summary>
internal sealed class NumericAnalyzerSelfInventoryTests
{
    [Test]
    public async Task AnalyzerAndAnalyzerTestsSourcesSatisfyNumericPolicyAsync()
    {
        var root = FindRepositoryRoot();
        var analyzerSources = ReadSources(Path.Combine(root, "src", "KeyLoad.Analyzers"));
        var testProject = Path.Combine(root, "tests", "KeyLoad.Analyzers.Tests");
        var testSources = ReadSources(testProject)
            .Append(ReadGeneratedTestGlobalUsings(testProject))
            .ToArray();
        var analyzers = new Microsoft.CodeAnalysis.Diagnostics.DiagnosticAnalyzer[]
        {
            new FileCodeLineCountAnalyzer(),
            new AggregateTypeCodeLineCountAnalyzer(),
            new ExecutableUnitCodeLineCountAnalyzer(),
            new ControlFlowNestingAnalyzer()
        };

        foreach (var analyzer in analyzers)
        {
            var analyzerFindings = await NumericAnalyzerFixture.AnalyzeSourcesAsync(
                analyzer, "KeyLoad.Analyzers", analyzerSources);
            var testFindings = await NumericAnalyzerFixture.AnalyzeSourcesAsync(
                analyzer, "KeyLoad.Analyzers.Tests", testSources);
            await Assert.That(analyzerFindings).IsEmpty();
            await Assert.That(testFindings).IsEmpty();
        }
    }

    private static (string Path, string Text)[] ReadSources(string projectDirectory) =>
        Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(static path => !path.Contains(
                    $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                !path.Contains(
                    $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(static path => (Path.GetFullPath(path), File.ReadAllText(path)))
            .ToArray();

    private static (string Path, string Text) ReadGeneratedTestGlobalUsings(string projectDirectory)
    {
        var output = new DirectoryInfo(AppContext.BaseDirectory);
        var targetFramework = output.Name;
        var configuration = output.Parent?.Name ??
            throw new DirectoryNotFoundException("The analyzer test output configuration is missing.");
        var generatedPath = Path.Combine(
            projectDirectory,
            "obj",
            configuration,
            targetFramework,
            "KeyLoad.Analyzers.Tests.GlobalUsings.g.cs");
        return (generatedPath, File.ReadAllText(generatedPath));
    }

    private static string FindRepositoryRoot()
    {
        var candidate = new DirectoryInfo(AppContext.BaseDirectory);
        while (candidate is not null && !File.Exists(Path.Combine(candidate.FullName, "KeyLoad.slnx")))
        {
            candidate = candidate.Parent;
        }

        return candidate?.FullName ?? throw new DirectoryNotFoundException(
            "Could not find KeyLoad.slnx above the analyzer test output directory.");
    }
}
