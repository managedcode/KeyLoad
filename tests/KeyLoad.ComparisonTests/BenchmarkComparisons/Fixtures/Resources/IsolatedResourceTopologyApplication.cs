using System.Globalization;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using KeyLoad.Comparisons;
using KeyLoad.LoadTests.AppHost.Features.BenchmarkComparisons;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class IsolatedResourceTopologyApplication : IAsyncDisposable
{
    private const string TemporaryPrefix = "keyload-isolated-route-";
    private const string ProductionDirectory = "production";
    private const string ReportsDirectory = "reports";
    private DistributedApplication? application;
    internal string Root { get; } = ResolveTemporaryDirectory(Directory.CreateTempSubdirectory(TemporaryPrefix));
    internal string ProductionRoot => Path.Combine(Root, ProductionDirectory);
    internal string Output => Path.Combine(Root, ReportsDirectory);
    internal LoadTestClientSettings Client => application!.Services.GetRequiredService<LoadTestClientSettings>();

    internal static string ResolveTemporaryDirectory(DirectoryInfo directory)
    {
        if (directory.Parent is null)
        {
            return directory.FullName;
        }
        var entry = new DirectoryInfo(Path.Combine(ResolveTemporaryDirectory(directory.Parent), directory.Name));
        return entry.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? entry.FullName;
    }

    internal async Task<IResource[]> BuildAsync(string? target, int count,
        string? invalidSetting = null, string? invalidValue = null)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var arguments = new List<string>();
        if (target is null)
        {
            arguments.Add("--KeyLoad:DataRoot=" + ProductionRoot);
            arguments.Add("--KeyLoad:Ephemeral=true");
            arguments.Add("--KeyLoad:SiloCount=" + count.ToString(CultureInfo.InvariantCulture));
            arguments.Add("--KeyLoad:ReplicaCount=" + count.ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            arguments.Add("--Benchmarks:DataRoot=" + Root);
            arguments.Add("--Benchmarks:Output=" + Output);
            arguments.Add("--Benchmarks:Session=true");
            arguments.Add("--Benchmarks:SessionId=resource-model");
            arguments.Add("--Benchmarks:EvidenceProfile=" + BenchmarkSessionContract.Profile);
            AddSelection(arguments, target, count);
        }
        if (invalidSetting is not null)
        {
            arguments.Add("--" + invalidSetting + "=" + invalidValue);
        }
        var builder = target is null
            ? await DistributedApplicationTestingBuilder.CreateAsync<Projects.KeyLoad_AppHost>([.. arguments], token)
            : await DistributedApplicationTestingBuilder.CreateAsync<Projects.KeyLoad_LoadTests_AppHost>([.. arguments], token);
        application = await builder.BuildAsync(token);
        var resources = application.Services.GetRequiredService<DistributedApplicationModel>().Resources;
        return target is null or "KeyLoad" ? resources.OfType<ProjectResource>().Cast<IResource>().ToArray()
            : resources.OfType<ContainerResource>().Cast<IResource>().ToArray();
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
