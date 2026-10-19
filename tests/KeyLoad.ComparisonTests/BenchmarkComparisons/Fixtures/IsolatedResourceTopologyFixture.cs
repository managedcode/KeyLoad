using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.Comparisons;
using KeyLoad.LoadTests.AppHost.Features.BenchmarkComparisons;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class IsolatedResourceTopologyFixture : IAsyncDisposable
{
    internal const string RunnerName = "comparisons";
    internal const string DataMount = "/data";
    internal const string RuntimePrefix = "Benchmarks__Execution__";
    private const string TemporaryPrefix = "keyload-isolated-model-";
    private const string SolutionFile = "KeyLoad.slnx";
    private const string AppHostDirectory = "build/KeyLoad.LoadTests.AppHost";
    private DistributedApplication? application;

    internal IsolatedResourceTopologyFixture(string target, int nodes, string? projectDirectory = null)
    {
        var temporary = IsolatedResourceTopologyApplication.ResolveTemporaryDirectory(new DirectoryInfo(Path.GetTempPath()));
        var root = Path.Combine(temporary, TemporaryPrefix + Guid.NewGuid().ToString("N"));
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions
        {
            DisableDashboard = true,
            ProjectDirectory = projectDirectory ?? Path.Combine(RepositoryRoot(), AppHostDirectory)
        });
        var selection = new BenchmarkSessionSelection(target, nodes);
        Context = new(builder, selection, new LoadTestClientSettings(), root);
    }

    internal IsolatedResourceContext Context
    {
        get;
    }

    internal ContainerResource[] Build()
    {
        LoadTestResourceLifetime.Apply(Context.Builder);
        application = Context.Builder.Build();
        return application.Services.GetRequiredService<DistributedApplicationModel>().Resources
            .OfType<ContainerResource>().ToArray();
    }

    internal ProjectResource[] Projects => application!.Services.GetRequiredService<DistributedApplicationModel>()
        .Resources.OfType<ProjectResource>().ToArray();

    internal static Task<Dictionary<string, string>> EnvironmentAsync(LoadTestClientSettings client)
        => Task.FromResult(client.Values.ToDictionary(pair => pair.Key, pair => pair.Value is IManifestExpressionProvider expression
            ? expression.ValueExpression : (string)pair.Value, StringComparer.Ordinal));

    internal static async Task<Dictionary<string, string>> EnvironmentAsync(IResource resource)
        => (await ConfigurationAsync(resource)).EnvironmentVariables.ToDictionary();

    internal static async Task<IExecutionConfigurationResult> ConfigurationAsync(IResource resource)
    {
        var result = await ExecutionConfigurationBuilder.Create(resource).WithEnvironmentVariablesConfig()
            .WithArgumentsConfig().BuildAsync(new DistributedApplicationExecutionContext(DistributedApplicationOperation.Publish),
                NullLogger.Instance, TestContext.Current!.Execution.CancellationToken);
        await Assert.That(result.Exception).IsNull();
        return result;
    }

    internal static async Task VerifyPrivateDataAsync(ContainerResource resource)
    {
        var mount = resource.Annotations.OfType<ContainerMountAnnotation>().Single(item => item.Target == DataMount);
        await Assert.That(mount.Type).IsEqualTo(ContainerMountType.Volume);
        await Assert.That(mount.IsReadOnly).IsFalse();
        await Assert.That(mount.Source).IsNull();
        await Assert.That(resource.Annotations.OfType<ContainerLifetimeAnnotation>().Single().Lifetime)
            .IsEqualTo(ContainerLifetime.Session);
    }

    internal static async Task VerifyUserAsync(ContainerResource resource)
    {
        var arguments = new List<object>();
        var context = new ContainerRuntimeArgsCallbackContext(arguments, TestContext.Current!.Execution.CancellationToken);
        foreach (var annotation in resource.Annotations.OfType<ContainerRuntimeArgsCallbackAnnotation>())
        {
            await annotation.Callback(context);
        }
        await Assert.That(arguments.Select(argument => (string)argument)).DoesNotContain("--user");
    }

    internal static async Task VerifyWaitsAsync(LoadTestClientSettings client, IEnumerable<IResource> nodes)
    {
        var names = nodes.Select(node => node.Name).ToArray();
        await Assert.That(client.Resources.Count).IsEqualTo(names.Length);
        await Assert.That(names.All(client.Resources.Contains)).IsTrue();
    }

    public async ValueTask DisposeAsync()
    {
        if (application is not null)
        {
            await application.DisposeAsync();
        }
        if (Directory.Exists(Context.Root))
        {
            Directory.Delete(Context.Root, recursive: true);
        }
    }

    private static string RepositoryRoot()
    {
        for (var directory = AppContext.BaseDirectory; directory is not null; directory = Path.GetDirectoryName(directory))
        {
            if (File.Exists(Path.Combine(directory, SolutionFile)))
            {
                return directory;
            }
        }
        throw new InvalidOperationException("The model test requires the source checkout.");
    }
}
