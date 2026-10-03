using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.BenchmarkComparisons;
using KeyLoad.Comparisons;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class IsolatedResourceTopologyFixture : IAsyncDisposable
{
    internal const string RunnerName = "comparisons";
    internal const string DataMount = "/data";
    internal const string NativePrefix = "Benchmarks__Native__";
    internal const string User = "1001:1001";
    internal const string ServerImage = "ghcr.io/managedcode/keyload-server:model@sha256:"
        + "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string TemporaryPrefix = "keyload-isolated-model-";
    private const string SolutionFile = "KeyLoad.slnx";
    private const string AppHostDirectory = "src/KeyLoad.AppHost";
    private DistributedApplication? application;

    internal IsolatedResourceTopologyFixture(string target, int nodes, string? projectDirectory = null)
    {
        var root = Path.Combine(Path.GetTempPath(), TemporaryPrefix + Guid.NewGuid().ToString("N"));
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions
        {
            DisableDashboard = true,
            ProjectDirectory = projectDirectory ?? Path.Combine(RepositoryRoot(), AppHostDirectory),
            Args = [$"--KeyLoad:ContainerUser={User}", $"--KeyLoad:ContainerImages:Server={ServerImage}"]
        });
        var selection = new ComparisonWorkerSelection(target, nodes, Scenario.PointRead,
            IsolatedComparisonContract.Current.Profile);
        var runner = builder.AddContainer(RunnerName, "model-runner", "model");
        Context = new(builder, selection, runner, root);
    }

    internal IsolatedResourceContext Context { get; }

    internal ContainerResource[] Build()
    {
        application = Context.Builder.Build();
        return application.Services.GetRequiredService<DistributedApplicationModel>().Resources
            .OfType<ContainerResource>().ToArray();
    }

    internal static async Task<Dictionary<string, string>> EnvironmentAsync(ContainerResource resource)
        => (await ConfigurationAsync(resource)).EnvironmentVariables.ToDictionary();

    internal static async Task<IExecutionConfigurationResult> ConfigurationAsync(ContainerResource resource)
    {
        var result = await ExecutionConfigurationBuilder.Create(resource).WithEnvironmentVariablesConfig()
            .WithArgumentsConfig().BuildAsync(new DistributedApplicationExecutionContext(DistributedApplicationOperation.Publish),
                NullLogger.Instance, TestContext.Current!.Execution.CancellationToken);
        await Assert.That(result.Exception).IsNull();
        return result;
    }

    internal static async Task VerifyPrivateDataAsync(ContainerResource resource, string root, string? parent = null)
    {
        var mount = resource.Annotations.OfType<ContainerMountAnnotation>().Single(item => item.Target == DataMount);
        await Assert.That(mount.IsReadOnly).IsFalse();
        await Assert.That(mount.Source).IsNotNull();
        var expected = parent is null ? Path.Combine(root, "native", resource.Name)
            : Path.Combine(root, "native", parent, resource.Name);
        await Assert.That(mount.Source).IsEqualTo(expected);
        await Assert.That(Path.GetFullPath(mount.Source!).StartsWith(root + Path.DirectorySeparatorChar,
            StringComparison.Ordinal)).IsTrue();
        await Assert.That(Directory.Exists(mount.Source)).IsTrue();
        if (!OperatingSystem.IsWindows())
        {
            await Assert.That(File.GetUnixFileMode(mount.Source!)).IsEqualTo(
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    internal static async Task VerifyUserAsync(ContainerResource resource)
    {
        var arguments = new List<object>();
        var context = new ContainerRuntimeArgsCallbackContext(arguments, TestContext.Current!.Execution.CancellationToken);
        foreach (var annotation in resource.Annotations.OfType<ContainerRuntimeArgsCallbackAnnotation>())
        {
            await annotation.Callback(context);
        }
        await Assert.That(arguments.Select(argument => (string)argument)).IsEquivalentTo(new[] { "--user", User });
    }

    internal static async Task VerifyWaitsAsync(ContainerResource runner, ContainerResource[] nodes)
    {
        var waits = runner.Annotations.OfType<WaitAnnotation>().Select(item => item.Resource.Name).ToArray();
        await Assert.That(waits.Length).IsEqualTo(nodes.Length);
        await Assert.That(nodes.All(node => waits.Contains(node.Name, StringComparer.Ordinal))).IsTrue();
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
