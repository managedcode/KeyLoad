using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.BenchmarkComparisons;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class IsolatedTimeSeriesTimescaleResourceTests
{
    private const string NodePrefix = "isolated-timescale-";

    /// <summary>AC-TSI-001/003: each requested count is represented by one real Timescale primary and physical standbys.</summary>
    [Test]
    [Arguments(1)]
    [Arguments(3)]
    public async Task ModelContainsPinnedPhysicalTimescaleNodes(int count)
    {
        await using var model = new IsolatedTimeSeriesTimescaleResourceModel();
        IsolatedTimeSeriesTimescaleResources.Add(model.Context(count));
        var nodes = model.Build().Where(resource => resource.Name.StartsWith(NodePrefix, StringComparison.Ordinal)).ToArray();
        await Assert.That(nodes.Length).IsEqualTo(count);
        for (var index = 0; index < count; index++)
        {
            await IsolatedTimeSeriesTimescaleResourceAssertions.VerifyNodeAsync(model.Root,
                nodes.Single(resource => resource.Name == IsolatedTimeSeriesTimescaleResourceAssertions.NodeName(index)), index, count);
        }
        await IsolatedTimeSeriesTimescaleResourceAssertions.VerifyUniqueNativePlacementAsync(nodes, count);
        await IsolatedTimeSeriesTimescaleResourceAssertions.VerifySharedCredentialAsync(model.Builder, nodes);
        await IsolatedTimeSeriesTimescaleResourceAssertions.VerifyRunnerBindingsAsync(model.Runner.Resource, model.Builder, nodes, count);
    }

    /// <summary>AC-TSI-001: malformed node counts fail before any native resource is allocated.</summary>
    [Test]
    [Arguments(0)]
    [Arguments(4)]
    public async Task InvalidNodeCountIsRejectedBeforeNativeResources(int count)
    {
        await using var model = new IsolatedTimeSeriesTimescaleResourceModel();
        var before = model.Builder.Resources.Count;
        await Assert.That(() => model.Context(count)).Throws<InvalidOperationException>();
        await Assert.That(model.Builder.Resources.Count).IsEqualTo(before);
    }

}

internal static class IsolatedTimeSeriesTimescaleResourceAssertions
{
    private const string PasswordName = "isolated-timescale-password";
    private const string Image = "timescale/timescaledb";
    private const string Digest = "sha256:e72689191e1c977892c53d6f2c344dbc4a9657a867dc8cc1899229f9d3672b2e";
    private const string ImageReference = "docker.io/timescale/timescaledb:2.30.2-pg18@sha256:e72689191e1c977892c53d6f2c344dbc4a9657a867dc8cc1899229f9d3672b2e";
    private const string RenderedImageReference = "docker.io/timescale/timescaledb@sha256:e72689191e1c977892c53d6f2c344dbc4a9657a867dc8cc1899229f9d3672b2e";
    private const string Tcp = "tcp";
    private const string DataTarget = "/var/lib/postgresql";
    private const string BootstrapTarget = "/bootstrap/isolated-postgres.sh";
    private const string InitTarget = "/docker-entrypoint-initdb.d/isolated-replication.sh";
    private const string PrimaryHostVariable = "KEYLOAD_POSTGRES_PRIMARY";
    private const string PrimaryHost = "isolated-timescale-1";
    private const string ExpectedPgDataKey = "PGDATA";
    private const string ExpectedStandbyCountKey = "KEYLOAD_POSTGRES_STANDBYS";
    private const string ExpectedApplicationNameKey = "PGAPPNAME";
    private const string ExpectedPasswordKey = "PGPASSWORD";
    private const string ExpectedImageBindingKey = "Benchmarks__Native__Image";
    private const string ExpectedConnectionBindingKey = "Benchmarks__Native__ConnectionString";
    private const string ExpectedPasswordBindingKey = "Benchmarks__Native__Password";
    private const int Port = 5432;
    private const int GroupLength = 32;

    internal static string NodeName(int index)
        => "isolated-timescale-" + (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);

    internal static async Task VerifyNodeAsync(string root, ContainerResource node, int index, int count)
    {
        var name = NodeName(index);
        await VerifyImageAndAliasAsync(node, name);
        await VerifyPrivatePlacementAsync(root, node, name);
        await VerifyNativeConfigurationAsync(node, index, count);
    }

    private static async Task VerifyImageAndAliasAsync(ContainerResource node, string name)
    {
        var image = node.Annotations.OfType<ContainerImageAnnotation>().Single();
        await Assert.That(image.Registry).IsEqualTo("docker.io");
        await Assert.That(image.Image).IsEqualTo(Image);
        await Assert.That(image.Tag).IsNull();
        await Assert.That(image.SHA256).IsEqualTo(Digest[7..]);
        await Assert.That(node.TryGetContainerImageName(out var renderedImage)).IsTrue();
        await Assert.That(renderedImage).IsEqualTo(RenderedImageReference);
        await Assert.That(node.Annotations.OfType<ContainerNetworkAliasAnnotation>().Single().Alias).IsEqualTo(name);
    }

    private static async Task VerifyPrivatePlacementAsync(string root, ContainerResource node, string name)
    {
        var containerName = node.Annotations.OfType<ContainerNameAnnotation>().Single().Name;
        await Assert.That(containerName.StartsWith(name + "-", StringComparison.Ordinal)).IsTrue();
        var group = containerName[(name.Length + 1)..];
        await Assert.That(group.Length).IsEqualTo(GroupLength);
        await Assert.That(group.All(char.IsAsciiHexDigitLower)).IsTrue();
        var mount = node.Annotations.OfType<ContainerMountAnnotation>().Single(item => item.Target == DataTarget);
        await Assert.That(mount.IsReadOnly).IsFalse();
        await Assert.That(mount.Type).IsEqualTo(ContainerMountType.BindMount);
        await Assert.That(mount.Source).IsEqualTo(Path.Combine(root, "native", name));
        await Assert.That(Directory.Exists(mount.Source)).IsTrue();
        if (!OperatingSystem.IsWindows())
        {
            await Assert.That(File.GetUnixFileMode(mount.Source!)).IsEqualTo(
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        await Assert.That(node.Annotations.OfType<EndpointAnnotation>().Single(item => item.Name == Tcp).TargetPort)
            .IsEqualTo(Port);
    }

    private static async Task VerifyNativeConfigurationAsync(ContainerResource node, int index, int count)
    {
        var config = await ConfigurationAsync(node);
        var arguments = config.Arguments.Select(argument => argument.Value).ToArray();
        await Assert.That(node.Entrypoint).IsEqualTo("/bin/sh");
        await Assert.That(arguments.SequenceEqual(new[] { BootstrapTarget, "postgres", "-c", "fsync=on", "-c",
            "synchronous_commit=on", "-c", "max_slot_wal_keep_size=512MB" })).IsTrue();
        var environment = config.EnvironmentVariables.ToDictionary();
        await Assert.That(environment[ExpectedPgDataKey]).IsEqualTo("/var/lib/postgresql/18/docker");
        await VerifyEntryMountAsync(node);
        if (index == 0)
        {
            await VerifyPrimaryBootstrapAsync(node);
            await Assert.That(environment.ContainsKey(PrimaryHostVariable)).IsFalse();
            await Assert.That(environment[ExpectedStandbyCountKey])
                .IsEqualTo((count - 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        else
        {
            await Assert.That(environment[PrimaryHostVariable]).IsEqualTo(PrimaryHost);
            await Assert.That(environment[ExpectedApplicationNameKey]).IsEqualTo("benchmark_standby" + index.ToString(System.Globalization.CultureInfo.InvariantCulture));
            await Assert.That(node.Annotations.OfType<ContainerMountAnnotation>().Any(item => item.Target == InitTarget)).IsFalse();
            await Assert.That(node.Annotations.OfType<WaitAnnotation>().Single().Resource.Name).IsEqualTo(PrimaryHost);
        }
    }

    private static async Task VerifyEntryMountAsync(ContainerResource node)
    {
        var entry = node.Annotations.OfType<ContainerMountAnnotation>().Single(item => item.Target == BootstrapTarget);
        await Assert.That(entry.IsReadOnly).IsTrue();
        await Assert.That(Path.GetFileName(entry.Source)).IsEqualTo("IsolatedPostgresEntry.sh");
    }

    private static async Task VerifyPrimaryBootstrapAsync(ContainerResource node)
    {
        var init = node.Annotations.OfType<ContainerMountAnnotation>().Single(item => item.Target == InitTarget);
        await Assert.That(init.IsReadOnly).IsTrue();
        await Assert.That(Path.GetFileName(init.Source)).IsEqualTo("IsolatedPostgresReplication.sh");
    }

    internal static async Task VerifyUniqueNativePlacementAsync(ContainerResource[] nodes, int count)
    {
        var containerNames = nodes.Select(node => node.Annotations.OfType<ContainerNameAnnotation>().Single().Name).ToArray();
        await Assert.That(containerNames.Distinct(StringComparer.Ordinal).Count()).IsEqualTo(count);
        await Assert.That(containerNames.Select(name => name[(name.LastIndexOf('-') + 1)..])
            .Distinct(StringComparer.Ordinal).Count()).IsEqualTo(1);
        await Assert.That(nodes.Select(node => node.Annotations.OfType<ContainerMountAnnotation>()
            .Single(item => item.Target == DataTarget).Source).Distinct(StringComparer.Ordinal).Count()).IsEqualTo(count);
    }

    internal static async Task VerifySharedCredentialAsync(IDistributedApplicationBuilder builder, ContainerResource[] nodes)
    {
        var parameters = builder.Resources.OfType<ParameterResource>().ToArray();
        var password = parameters.Single(parameter => parameter.Name == PasswordName);
        await Assert.That(parameters.Count(parameter => parameter.Secret)).IsEqualTo(1);
        await Assert.That(password.Secret).IsTrue();
        var value = await password.GetValueAsync(TestContext.Current!.Execution.CancellationToken);
        await Assert.That(value).IsNotNull();
        await Assert.That(value!.Length).IsEqualTo(64);
        await Assert.That(value.All(char.IsAsciiHexDigit)).IsTrue();
        foreach (var node in nodes)
        {
            var environment = await ConfigurationAsync(node);
            var bindings = environment.EnvironmentVariablesWithUnprocessed.ToDictionary(pair => pair.Key, pair => pair.Value.Unprocessed);
            await Assert.That(ReferenceEquals(bindings[ExpectedPasswordKey], password)).IsTrue();
        }
    }

    internal static async Task VerifyRunnerBindingsAsync(ContainerResource runner, IDistributedApplicationBuilder builder,
        ContainerResource[] nodes, int count)
    {
        var config = await ConfigurationAsync(runner);
        var environment = config.EnvironmentVariables.ToDictionary();
        await Assert.That(environment.Keys.Count(key => key.StartsWith("Benchmarks__Native__Endpoints__", StringComparison.Ordinal)))
            .IsEqualTo(count);
        await Assert.That(environment[ExpectedImageBindingKey]).IsEqualTo(ImageReference);
        await Assert.That(environment[ExpectedConnectionBindingKey])
            .IsEqualTo(((PostgresServerResource)nodes[0]).ConnectionStringExpression.ValueExpression);
        var bindings = config.EnvironmentVariablesWithUnprocessed.ToDictionary(pair => pair.Key, pair => pair.Value.Unprocessed);
        await Assert.That(bindings.ContainsKey(ExpectedPasswordBindingKey)).IsFalse();
        for (var index = 0; index < count; index++)
        {
            var key = "Benchmarks__Native__Endpoints__" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            await Assert.That(environment[key]).IsEqualTo("{" + nodes[index].Name + ".bindings.tcp.url}");
            await Assert.That(bindings[key]).IsTypeOf<EndpointReference>();
            var reference = (EndpointReference)bindings[key];
            await Assert.That(ReferenceEquals(reference.Resource, nodes[index])).IsTrue();
            await Assert.That(reference.EndpointName).IsEqualTo(Tcp);
        }
        await Assert.That(builder.Resources.OfType<ParameterResource>().Count(parameter => parameter.Name == PasswordName)).IsEqualTo(1);
    }

    private static async Task<IExecutionConfigurationResult> ConfigurationAsync(ContainerResource resource)
    {
        var result = await ExecutionConfigurationBuilder.Create(resource).WithEnvironmentVariablesConfig()
            .WithArgumentsConfig().BuildAsync(new DistributedApplicationExecutionContext(DistributedApplicationOperation.Publish),
                NullLogger.Instance, TestContext.Current!.Execution.CancellationToken);
        await Assert.That(result.Exception).IsNull();
        return result;
    }
}

internal sealed class IsolatedTimeSeriesTimescaleResourceModel : IAsyncDisposable
{
    private const string SolutionFile = "KeyLoad.slnx";
    private const string AppHostDirectory = "src/KeyLoad.AppHost";
    private DistributedApplication? application;

    internal IsolatedTimeSeriesTimescaleResourceModel()
    {
        Root = Directory.CreateTempSubdirectory("keyload-timescale-model-").FullName;
        Builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions
        {
            DisableDashboard = true,
            ProjectDirectory = Path.Combine(RepositoryRoot(), AppHostDirectory),
            Args = []
        });
        Runner = Builder.AddContainer("comparisons", "timescale-model-runner", "model");
    }

    internal IDistributedApplicationBuilder Builder { get; }
    internal IResourceBuilder<ContainerResource> Runner { get; }
    internal string Root { get; }
    internal IsolatedTimeSeriesResourceContext Context(int count) => new(Builder, count, Runner, Root);

    internal ContainerResource[] Build()
    {
        application = Builder.Build();
        return application.Services.GetRequiredService<DistributedApplicationModel>().Resources
            .OfType<ContainerResource>().ToArray();
    }

    public async ValueTask DisposeAsync()
    {
        if (application is not null)
        {
            await application.DisposeAsync();
        }
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
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
        throw new InvalidOperationException("The resource model requires the source checkout.");
    }
}
