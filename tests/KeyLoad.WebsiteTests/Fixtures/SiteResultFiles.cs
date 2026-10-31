using System.Text.Json.Nodes;

namespace KeyLoad.WebsiteTests.BenchmarkComparisons;

/// <summary>Locates the repository and the benchmark result test files under this project's Fixtures/Results folder.</summary>
/// <remarks>
/// Each folder mirrors one downloaded Benchmarks job artifact: its <c>&lt;database&gt;-&lt;type&gt;-n&lt;nodes&gt;.json</c>
/// result file and, for some jobs, a test-results folder. The values are test data and never published.
/// </remarks>
internal static class SiteResultFiles
{
    private const string SolutionFile = "KeyLoad.slnx";
    private const string JsonPattern = "*.json";
    private static readonly Lazy<string> RepositoryRoot = new(FindRepository);

    internal static string Repository => RepositoryRoot.Value;

    internal static string Root => Path.Combine(Repository, "tests", "KeyLoad.WebsiteTests", "Fixtures", "Results");

    internal static string[] Artifacts => [.. Directory.GetDirectories(Root).Select(Path.GetFileName).OfType<string>()
        .Order(StringComparer.Ordinal)];

    internal static string ResultPath(string artifact)
        => Directory.GetFiles(Path.Combine(Root, artifact), JsonPattern, SearchOption.TopDirectoryOnly).Single();

    internal static string FileName(string artifact) => Path.GetFileName(ResultPath(artifact));

    internal static JsonObject Read(string artifact) => JsonNode.Parse(File.ReadAllText(ResultPath(artifact)))!.AsObject();

    private static string FindRepository()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFile)))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("The KeyLoad repository root was not found above the test output.");
    }
}
