using System.Diagnostics;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class SiteQualificationStartupFixture : IAsyncDisposable
{
    internal const string Sentinel = "original filesystem bytes";
    private const string ActionPath = ".github/workflows/Features/BenchmarkComparisons/QualifySite/action.yml";
    private const string Step = "    - name: Create website test folders";
    private const string Run = "      run: |";
    private const string Indent = "        ";
    private const string Capture = "site-isolated-github-cli.mjs capture";
    private const string PathEnvironment = "PATH";
    private const string WorkspaceEnvironment = "GITHUB_WORKSPACE";
    private const string EnvironmentPath = "GITHUB_ENV";
    private static readonly TimeSpan RunBound = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan CleanupBound = TimeSpan.FromSeconds(5);
    private readonly string root;
    private Task? actualExit;
    private string? link;

    internal SiteQualificationStartupFixture(int scenario)
    {
        root = Directory.CreateTempSubdirectory("keyload-site-startup-").FullName;
        Workspace = Directory.CreateDirectory(Path.Combine(root, "workspace")).FullName;
        EnvironmentFile = Path.Combine(root, "github-env");
        Target = Directory.CreateDirectory(Path.Combine(root, "retained-target")).FullName;
        File.WriteAllText(Path.Combine(Target, "sentinel"), Sentinel);
        Prepare(scenario);
    }

    internal string Workspace { get; }
    internal string EnvironmentFile { get; }
    internal string Target { get; }
    internal string Parent => Path.Combine(Workspace, "artifacts");
    internal string Base => Path.Combine(Parent, "site-evidence");
    internal string Envelope => Path.Combine(Base, "isolated-capture-envelope.json");
    internal string CaptureDirectory => Path.Combine(Base, "isolated-capture");
    internal string ExistingSentinel => Path.Combine(Base, "sentinel");
    internal string? LinkTarget => link is null ? null : new DirectoryInfo(link).LinkTarget;

    internal async Task<IsolatedAggregateNodeResult> RunAsync(CancellationToken token)
    {
        var start = CreateStartInfo();
        using var owner = IsolatedAggregateNodeStartOwner.Create(start);
        var process = owner.StartAndTransfer();
        actualExit = process.WaitForExitAsync(CancellationToken.None);
        return await IsolatedAggregateNodeLifetime.RunAsync(process, RunBound, CleanupBound, token);
    }

    public async ValueTask DisposeAsync()
    {
        if (actualExit is not null)
        {
            await actualExit.WaitAsync(CleanupBound);
        }
        if (link is not null)
        {
            Directory.Delete(link);
        }
        Directory.Delete(root, recursive: true);
    }

    internal string ExpectedEnvironment() =>
        $"EVIDENCE_DIR={Base}\nKEYLOAD_SITE_REPOSITORY={Workspace}/website\n" +
        $"KEYLOAD_SITE_COVERAGE={Base}/js-coverage\n" +
        $"KEYLOAD_SITE_ISOLATED_CAPTURE={CaptureDirectory}\n" +
        $"KEYLOAD_SITE_ISOLATED_ARCHIVE_RECEIPT={CaptureDirectory}/archive-receipt.json\n" +
        $"KEYLOAD_SITE_ISOLATED_AGGREGATE={CaptureDirectory}/input/aggregate\n";

    private void Prepare(int scenario)
    {
        switch (scenario)
        {
            case 0:
                break;
            case 1:
                Directory.CreateDirectory(Base);
                File.WriteAllText(ExistingSentinel, Sentinel);
                break;
            case 2:
                Directory.CreateDirectory(Parent);
                File.WriteAllText(Base, Sentinel);
                break;
            case 3:
                File.WriteAllText(Parent, Sentinel);
                break;
            case 4:
                Directory.CreateDirectory(Parent);
                CreateLink(Base);
                break;
            case 5:
                CreateLink(Parent);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }
        if (scenario >= 2)
        {
            File.WriteAllText(EnvironmentFile, Sentinel);
        }
    }

    private void CreateLink(string path)
    {
        Directory.CreateSymbolicLink(path, Target);
        link = path;
    }

    private ProcessStartInfo CreateStartInfo()
    {
        var start = new ProcessStartInfo("/bin/bash")
        {
            WorkingDirectory = Workspace,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.Environment.Clear();
        start.Environment[PathEnvironment] = Environment.GetEnvironmentVariable(PathEnvironment);
        start.Environment[WorkspaceEnvironment] = Workspace;
        start.Environment[EnvironmentPath] = EnvironmentFile;
        foreach (var argument in new[] { "--noprofile", "--norc", "-e", "-o", "pipefail", "-c", Script() })
        {
            start.ArgumentList.Add(argument);
        }
        return start;
    }

    private static string Script()
    {
        var lines = File.ReadAllLines(Path.Combine(IsolatedAggregateNodeProcess.RepositoryRoot(), ActionPath));
        var step = Array.IndexOf(lines, Step);
        if (step < 0 || lines[step + 2] != Run)
        {
            throw new InvalidOperationException("The actual qualification initialization block was not found.");
        }
        var body = lines.Skip(step + 3).TakeWhile(line => line.StartsWith(Indent, StringComparison.Ordinal));
        var capture = lines.Single(line => line.Contains(Capture, StringComparison.Ordinal));
        var redirect = capture.IndexOf(" > ", StringComparison.Ordinal);
        if (redirect < 0)
        {
            throw new InvalidOperationException("The actual capture envelope redirection was not found.");
        }
        // Open the actual envelope destination without invoking a provider or producing measurements.
        return string.Join('\n', body.Select(line => line[Indent.Length..])) +
            "\nEVIDENCE_DIR=\"$base\"\n:" + capture[redirect..];
    }
}
