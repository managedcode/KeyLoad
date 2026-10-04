using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace KeyLoad.ComparisonTests.Features.TestInfrastructure;

internal sealed class AspireFailureNotificationFixture : IAsyncDisposable
{
    internal const string RunnerName = "owned-runner";
    internal const string ServerName = "required-server";
    internal const string LeafName = "required-leaf";
    internal const string BootstrapName = "required-bootstrap";
    internal const string ConfigurationName = "owned-configuration";
    private const string Image = "mcr.microsoft.com/dotnet/runtime";
    private const string ImageTag = "10.0";

    internal AspireFailureNotificationFixture(bool includeDependencies = true, int bootstrapExit = 0,
        Func<BeforeStartEvent, CancellationToken, Task>? beforeStart = null)
    {
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions { DisableDashboard = true });
        Runner = builder.AddContainer(RunnerName, Image, ImageTag).Resource;
        Server = builder.AddContainer(ServerName, Image, ImageTag).Resource;
        Leaf = builder.AddContainer(LeafName, Image, ImageTag).Resource;
        Bootstrap = builder.AddContainer(BootstrapName, Image, ImageTag).Resource;
        Configuration = builder.AddResource(new AspireFailureConfigurationResource(ConfigurationName)).Resource;
        Runner.Annotations.Add(new WaitAnnotation(Configuration, WaitType.WaitUntilHealthy));
        if (includeDependencies)
        {
            Runner.Annotations.Add(new WaitAnnotation(Server, WaitType.WaitUntilHealthy)
            { WaitBehavior = WaitBehavior.StopOnResourceUnavailable });
            Runner.Annotations.Add(new WaitAnnotation(Bootstrap, WaitType.WaitForCompletion, bootstrapExit));
            Server.Annotations.Add(new WaitAnnotation(Leaf, WaitType.WaitUntilStarted)
            { WaitBehavior = WaitBehavior.StopOnResourceUnavailable });
        }
        if (beforeStart is not null)
        {
            builder.Eventing.Subscribe(beforeStart);
        }
        Application = builder.Build();
    }

    internal DistributedApplication Application { get; }
    internal ContainerResource Runner { get; }
    internal ContainerResource Server { get; }
    internal ContainerResource Leaf { get; }
    internal ContainerResource Bootstrap { get; }
    internal AspireFailureConfigurationResource Configuration { get; }
    internal ResourceNotificationService Notifications => Application.ResourceNotifications;

    internal Task PublishAsync(IResource resource, string state, int? exit = null, HealthStatus? health = null)
        => Notifications.PublishUpdateAsync(resource, snapshot => (snapshot with { State = state, ExitCode = exit })
            .WithHealthReports(health is { } status ? [new("native-health", status, null, null)] : []));

    internal void StopHost() => Application.Services.GetRequiredService<IHostApplicationLifetime>().StopApplication();

    public async ValueTask DisposeAsync() => await Application.DisposeAsync();
}

internal sealed class AspireFailureConfigurationResource(string name) : Resource(name), IResourceWithoutLifetime;
