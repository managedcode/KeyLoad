using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.BenchmarkComparisons;
using KeyLoad.Comparisons;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using T = KeyLoad.ComparisonTests.Features.BenchmarkComparisons.IsolatedQuorumResourceTokens;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class IsolatedQuorumResourceModel : IDisposable
{
    private DistributedApplication? application;
    internal string Root { get; } = Directory.CreateTempSubdirectory(T.TemporaryPrefix).FullName;
    internal IDistributedApplicationBuilder Builder { get; } = DistributedApplication.CreateBuilder(
        new DistributedApplicationOptions { DisableDashboard = true });
    internal IResourceBuilder<ContainerResource> Runner { get; }

    internal IsolatedQuorumResourceModel()
    {
        Runner = Builder.AddContainer(T.Runner, T.ModelImage);
    }

    internal void Add(string engine, int nodes)
        => Add(engine, new ComparisonWorkerSelection(engine, nodes, Scenario.PointRead, T.Profile));

    internal void Add(string helper, ComparisonWorkerSelection selection)
    {
        var context = new IsolatedResourceContext(Builder, selection, Runner, Root);
        if (helper == T.Qdrant)
        {
            IsolatedQdrantResources.Add(context);
            return;
        }
        IsolatedRabbitResources.Add(context);
    }

    internal ContainerResource[] BuildNodes()
    {
        application = Builder.Build();
        return application.Services.GetRequiredService<DistributedApplicationModel>().Resources
            .OfType<ContainerResource>().Where(resource => resource.Name != T.Runner).ToArray();
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
