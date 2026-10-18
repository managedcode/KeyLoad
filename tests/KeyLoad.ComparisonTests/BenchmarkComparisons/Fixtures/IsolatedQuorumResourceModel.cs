using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.LoadTests.AppHost.Features.BenchmarkComparisons;
using KeyLoad.Comparisons;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using T = KeyLoad.ComparisonTests.Features.BenchmarkComparisons.IsolatedQuorumResourceTokens;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class IsolatedQuorumResourceModel : IDisposable
{
    private DistributedApplication? application;
    internal string Root { get; } = Directory.CreateTempSubdirectory(T.TemporaryPrefix).FullName;
    internal IDistributedApplicationBuilder Builder
    {
        get;
    } = DistributedApplication.CreateBuilder(
        new DistributedApplicationOptions { DisableDashboard = true });
    internal LoadTestClientSettings Client { get; } = new();

    internal void Add(string engine, int nodes)
        => Add(engine, new BenchmarkSessionSelection(engine, nodes));

    internal void Add(string helper, BenchmarkSessionSelection selection)
    {
        var context = new IsolatedResourceContext(Builder, selection, Client, Root);
        if (helper == T.Qdrant)
        {
            IsolatedQdrantResources.Add(context);
            return;
        }
        IsolatedRabbitResources.Add(context);
    }

    internal ContainerResource[] BuildNodes()
    {
        LoadTestResourceLifetime.Apply(Builder);
        application = Builder.Build();
        return application.Services.GetRequiredService<DistributedApplicationModel>().Resources
            .OfType<ContainerResource>().ToArray();
    }

    internal static async Task<IExecutionConfigurationResult> ReadAsync(IResource resource)
    {
        var configuration = await ExecutionConfigurationBuilder.Create(resource).WithEnvironmentVariablesConfig()
            .WithArgumentsConfig().BuildAsync(new DistributedApplicationExecutionContext(DistributedApplicationOperation.Publish),
                NullLogger.Instance, TestContext.Current!.Execution.CancellationToken);
        await Assert.That(configuration.Exception).IsNull();
        return configuration;
    }

    public void Dispose()
    {
        application?.Dispose();
        Directory.Delete(Root, recursive: true);
    }
}
