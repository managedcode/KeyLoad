using System.Text.Json;

namespace KeyLoad.UnitTests.Features.RepositoryGovernance;

internal sealed class GovernanceRootFixture : IDisposable
{
    private const string RecordPath = "docs/implementation/mcaf-installation.json";
    private const string GovernanceFeature = "docs/Features/RepositoryGovernance.md";
    private const string GovernanceAdr = "docs/ADR/ADR-032-mcaf-governance.md";
    private const string Architecture = "docs/Architecture.md";
    private const string RootPolicy = "AGENTS.md";
    private const string LocalPolicy = "AGENTS.md";
    private const string ProjectsField = "projects";
    private const string ModulesField = "modules";
    private const string TemporaryDirectoryPrefix = "keyload-governance-";

    private GovernanceRootFixture(string root) => Root = root;

    internal string Root { get; }

    internal static async Task<GovernanceRootFixture> CreateAsync(CancellationToken cancellationToken)
    {
        GovernanceRootFixture? fixture = null;
        try
        {
            fixture = new GovernanceRootFixture(Path.Combine(Path.GetTempPath(),
                $"{TemporaryDirectoryPrefix}{Guid.NewGuid():N}"));
            Directory.CreateDirectory(fixture.Root);
            var source = GovernanceNodeProcess.RepositoryRoot;
            using var record = JsonDocument.Parse(await File.ReadAllTextAsync(
                Path.Combine(source, RecordPath), cancellationToken));
            var paths = new HashSet<string>(StringComparer.Ordinal)
            {
                RecordPath, RootPolicy, GovernanceFeature, GovernanceAdr, Architecture
            };
            AddProjectPaths(record.RootElement, paths);
            AddModulePaths(record.RootElement, paths);
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

    private static void AddProjectPaths(JsonElement record, HashSet<string> paths)
    {
        foreach (var project in record.GetProperty(ProjectsField).EnumerateArray())
        {
            var path = project.GetString()!;
            paths.Add(path);
            paths.Add(Path.Combine(Path.GetDirectoryName(path)!, LocalPolicy));
        }
    }

    private static void AddModulePaths(JsonElement record, HashSet<string> paths)
    {
        foreach (var module in record.GetProperty(ModulesField).EnumerateArray())
        {
            paths.Add(Path.Combine(module.GetString()!, LocalPolicy));
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
