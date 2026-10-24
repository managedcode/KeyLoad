using System.Xml.Linq;

namespace KeyLoad.UnitTests.RepositoryGovernance;

internal sealed class GovernanceRootFixture : IDisposable
{
    private const string SolutionPath = "KeyLoad.slnx";
    private const string GovernanceFeature = "docs/Features/RepositoryGovernance.md";
    private const string GovernanceAdr = "docs/ADR/ADR-032-mcaf-governance.md";
    private const string Architecture = "docs/Architecture.md";
    private const string RootPolicy = "AGENTS.md";
    private const string LocalPolicy = "AGENTS.md";
    private const string ProjectElement = "Project";
    private const string PathAttribute = "Path";
    private const string TemporaryDirectoryPrefix = "keyload-governance-";
    private static readonly string[] ModulePaths = ["docs", "site", "scripts", ".github/workflows", "analyzers", "build"];

    private GovernanceRootFixture(string root) => Root = root;

    internal string Root
    {
        get;
    }

    internal static async Task<GovernanceRootFixture> CreateAsync(CancellationToken cancellationToken)
    {
        GovernanceRootFixture? fixture = null;
        try
        {
            fixture = new GovernanceRootFixture(Path.Combine(AppContext.BaseDirectory,
                $"{TemporaryDirectoryPrefix}{Guid.NewGuid():N}"));
            Directory.CreateDirectory(fixture.Root);
            var source = GovernanceNodeProcess.RepositoryRoot;
            var solution = XDocument.Load(Path.Combine(source, SolutionPath));
            var paths = new HashSet<string>(StringComparer.Ordinal)
            {
                SolutionPath, RootPolicy, GovernanceFeature, GovernanceAdr, Architecture
            };
            AddProjectPaths(solution, paths);
            AddModulePaths(paths);
            await fixture.CopyRequiredFilesAsync(source, paths, cancellationToken);
            var completed = fixture;
            fixture = null;
            return completed;
        }
        finally
        {
            fixture?.Dispose();
        }
    }

    internal string PathFor(string relativePath) => Path.Combine(Root,
        relativePath.Replace('/', Path.DirectorySeparatorChar));

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }

    private static void AddProjectPaths(XDocument solution, HashSet<string> paths)
    {
        foreach (var project in solution.Descendants(ProjectElement))
        {
            var path = project.Attribute(PathAttribute)!.Value;
            paths.Add(path);
            paths.Add(Path.Combine(Path.GetDirectoryName(path)!, LocalPolicy));
        }
    }

    private static void AddModulePaths(HashSet<string> paths)
    {
        foreach (var module in ModulePaths)
        {
            paths.Add(Path.Combine(module, LocalPolicy));
        }
    }

    private async Task CopyRequiredFilesAsync(string source, IEnumerable<string> paths,
        CancellationToken cancellationToken)
    {
        foreach (var path in paths)
        {
            var destination = PathFor(path);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var input = File.OpenRead(Path.Combine(source, path));
            await using var output = File.Create(destination);
            await input.CopyToAsync(output, cancellationToken);
        }
    }
}
