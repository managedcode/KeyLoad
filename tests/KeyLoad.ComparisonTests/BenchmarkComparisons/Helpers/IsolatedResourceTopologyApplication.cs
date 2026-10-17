using System.Globalization;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using KeyLoad.Comparisons;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class IsolatedResourceTopologyApplication : IAsyncDisposable
{
    private const string TemporaryPrefix = "keyload-isolated-route-";
    private const string SourceEnvironment = "GITHUB_SHA";
    private const string RunnerImage = "ghcr.io/managedcode/keyload-comparisons:model@sha256:"
        + "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string ProductionDirectory = "production";
    private const string ReportsDirectory = "reports";
    private DistributedApplication? application;
    internal string Root { get; } = ResolveTemporaryDirectory(Directory.CreateTempSubdirectory(TemporaryPrefix));
    internal string ProductionRoot => Path.Combine(Root, ProductionDirectory);
    internal string Output => Path.Combine(Root, ReportsDirectory);
    internal static string Source => Environment.GetEnvironmentVariable(SourceEnvironment)
        ?? throw new InvalidOperationException("This model test requires the actual GitHub source revision.");

    internal static string ResolveTemporaryDirectory(DirectoryInfo directory)
    {
        if (directory.Parent is null)
        {
            return directory.FullName;
        }
        var entry = new DirectoryInfo(Path.Combine(ResolveTemporaryDirectory(directory.Parent), directory.Name));
        return entry.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? entry.FullName;
    }

    internal async Task<ContainerResource[]> BuildAsync(string? target, int count,
        string? invalidSetting = null, string? invalidValue = null)
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var arguments = new List<string>
        {
            $"--KeyLoad:DataRoot={ProductionRoot}", "--KeyLoad:Ephemeral=true",
            $"--KeyLoad:ContainerUser={IsolatedResourceTopologyFixture.User}",
            $"--KeyLoad:ContainerImages:Server={IsolatedResourceTopologyFixture.ServerImage}",
            $"--Benchmarks:ContainerImages:LoadGenerator={RunnerImage}",
            $"--Benchmarks:DataRoot={Root}", $"--Benchmarks:Output={Output}",
            "--Benchmarks:Session=true", "--Benchmarks:SessionId=resource-model",
            "--Benchmarks:EvidenceProfile=" + BenchmarkSessionContract.Profile
        };
        if (target is null)
        {
            arguments.Add("--Benchmarks:Enabled=false");
        }
        else
        {
            AddSelection(arguments, target, count);
        }
        if (invalidSetting is not null)
        {
            arguments.Add("--" + invalidSetting + "=" + invalidValue);
        }
        _ = Source;
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.KeyLoad_AppHost>(
            [.. arguments], cancellationToken);
        application = await builder.BuildAsync(cancellationToken);
        return application.Services.GetRequiredService<DistributedApplicationModel>().Resources
            .OfType<ContainerResource>().ToArray();
    }

    public async ValueTask DisposeAsync()
    {
        if (application is not null)
        {
            await application.DisposeAsync();
        }
        Directory.Delete(Root, recursive: true);
    }

    private static void AddSelection(List<string> arguments, string target, int count)
    {
        arguments.Add("--Benchmarks:Enabled=true");
        arguments.Add("--" + BenchmarkSessionSelection.TargetSetting + "=" + target);
        arguments.Add("--" + BenchmarkSessionSelection.NodeCountSetting + "=" + count.ToString(CultureInfo.InvariantCulture));
    }
}
