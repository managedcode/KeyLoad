using System.Text.Json;

namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

internal sealed class SiteAnalyzerCoverageTestScope : IDisposable
{
    private readonly string evidenceRoot;
    private readonly string sourceRepository;
    private readonly string tempRoot;

    internal SiteAnalyzerCoverageTestScope(bool copyRepository = false)
    {
        sourceRepository = FindRepositoryRoot();
        tempRoot = Path.Combine(Path.GetTempPath(), SiteAnalyzerCoverageTokens.CoverageDirectoryName, Guid.NewGuid().ToString("N"));
        Repository = copyRepository ? Path.Combine(tempRoot, SiteAnalyzerCoverageTokens.RepoDirectoryName) : sourceRepository;
        evidenceRoot = Path.Combine(tempRoot, SiteAnalyzerCoverageTokens.EvidenceDirectoryName);
        Directory.CreateDirectory(Repository);
        Directory.CreateDirectory(evidenceRoot);
        ScriptPath = Path.Combine(sourceRepository, SiteAnalyzerCoverageTokens.ScriptRelativePath);
        ContractPath = Path.Combine(sourceRepository, SiteAnalyzerCoverageTokens.ContractRelativePath);
        File.Copy(
            Path.Combine(sourceRepository, SiteAnalyzerCoverageTokens.SettingsSourcePath),
            Path.Combine(evidenceRoot, SiteAnalyzerCoverageTokens.SettingsCopyName));
        if (copyRepository)
        {
            CopyContractSources();
        }
    }

    internal string Repository { get; }
    internal string ManifestPath => Path.Combine(evidenceRoot, SiteAnalyzerCoverageTokens.ManifestName);
    internal string SummaryPath => Path.Combine(evidenceRoot, SiteAnalyzerCoverageTokens.SummaryName);
    internal string SettingsCopyPath => Path.Combine(evidenceRoot, SiteAnalyzerCoverageTokens.SettingsCopyName);
    private string ScriptPath { get; }
    private string ContractPath { get; }

    internal async Task PrepareAsync()
    {
        var result = await RunAsync(SiteAnalyzerCoverageTokens.ModePrepare);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(result.StandardError);
        }
    }

    internal Task WriteFixtureAsync(string fixture) =>
        File.WriteAllTextAsync(Path.Combine(evidenceRoot, SiteAnalyzerCoverageTokens.InputName), fixture);

    internal static JsonDocument ReadJson(string path) => JsonDocument.Parse(File.ReadAllText(path));

    internal Task<(int ExitCode, string StandardOutput, string StandardError)> RunAsync(
        string mode,
        bool includeCoverage = true,
        IReadOnlyDictionary<string, string?>? environmentOverrides = null)
    {
        var arguments = new List<string>
        {
            SiteAnalyzerCoverageTokens.NoLogo,
            SiteAnalyzerCoverageTokens.NoProfile,
            SiteAnalyzerCoverageTokens.NonInteractive,
            SiteAnalyzerCoverageTokens.FileArgument,
            ScriptPath,
            SiteAnalyzerCoverageTokens.ParameterMode,
            mode,
            SiteAnalyzerCoverageTokens.ParameterRepository,
            Repository,
            SiteAnalyzerCoverageTokens.ParameterContract,
            ContractPath,
            SiteAnalyzerCoverageTokens.ParameterEvidence,
            evidenceRoot
        };
        if (mode == SiteAnalyzerCoverageTokens.ModeVerify && includeCoverage)
        {
            arguments.Add(SiteAnalyzerCoverageTokens.ParameterCoverage);
            arguments.Add(Path.Combine(evidenceRoot, SiteAnalyzerCoverageTokens.InputName));
        }

        return SiteAnalyzerCoverageProcess.RunAsync(arguments, sourceRepository, environmentOverrides);
    }

    private void CopyContractSources()
    {
        using var contract = JsonDocument.Parse(File.ReadAllText(ContractPath));
        foreach (var source in contract.RootElement.GetProperty(SiteAnalyzerCoverageTokens.JsonSources).EnumerateArray())
        {
            CopyRepositoryFile(source.GetProperty(SiteAnalyzerCoverageTokens.PathProperty).GetString()!);
        }

        foreach (var config in contract.RootElement.GetProperty(SiteAnalyzerCoverageTokens.JsonConfiguration).EnumerateArray())
        {
            CopyRepositoryFile(config.GetString()!);
        }

        var helperDirectory = Path.Combine(sourceRepository, SiteAnalyzerCoverageTokens.HelperDirectory);
        foreach (var helper in Directory.EnumerateFiles(helperDirectory, SiteAnalyzerCoverageTokens.HelperFilePattern))
        {
            CopyRepositoryFile(Path.GetRelativePath(sourceRepository, helper));
        }
    }

    private void CopyRepositoryFile(string relativePath)
    {
        var source = Path.Combine(sourceRepository, relativePath);
        var destination = Path.Combine(Repository, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination);
    }

    private static string FindRepositoryRoot()
    {
        var repository = Environment.GetEnvironmentVariable(SiteAnalyzerCoverageTokens.RepositoryVariable);
        if (!string.IsNullOrWhiteSpace(repository) && File.Exists(Path.Combine(repository, SiteAnalyzerCoverageTokens.SolutionFileName)))
        {
            return Path.GetFullPath(repository);
        }

        var candidate = new DirectoryInfo(AppContext.BaseDirectory);
        while (candidate is not null && !File.Exists(Path.Combine(candidate.FullName, SiteAnalyzerCoverageTokens.SolutionFileName)))
        {
            candidate = candidate.Parent;
        }

        return candidate?.FullName ?? throw new DirectoryNotFoundException(SiteAnalyzerCoverageTokens.RepositoryUnavailableMessage);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempRoot))
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }
}
