using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using KeyLoad.ClusterTests.Messaging;
using KeyLoad.ClusterTests.StorageReplication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KeyLoad.ClusterTests;

internal sealed partial class ClusterFixture
{
    protected override TimeSpan ResourceTimeout => TimeSpan.FromMinutes(2);

    protected override string[] Args
        => ClusterFixtureComposition.PrepareArguments(Root, PublicQueryStorageSelection.Read(Key));

    protected override bool EnableTelemetryCollection => telemetry is null;

    protected override bool DumpResourceLogsOnFailure => false;

    protected override void ConfigureAppHost(DistributedApplicationOptions options, HostApplicationBuilderSettings settings)
        => options.DisableDashboard = telemetry is null;

    protected override void ConfigureBuilder(IDistributedApplicationTestingBuilder builder)
    {
        ClusterFixtureComposition.ConfigureTestOverrides(builder, maximumBodyBytes, queryExecution);
        queueDeadline?.Configure(builder);
        runtimeSql.Configure(builder);
        RemoteTransferCoordinatorFixtureConfiguration.Configure(builder, transferCoordinator);
        resourceNames = ClusterFixtureComposition.GetResourceNames(builder);
        ClusterFixtureComposition.ConfigureLogging(builder);
    }

    // The testing factory runs the AppHost entry point, including its before-start hooks, concurrently with the build
    // returning, so the fixture's application is taken here, where it is passed in, and not from a before-start hook.
    protected override async Task WaitForResourcesAsync(DistributedApplication app, CancellationToken cancellationToken)
    {
        App = app;
        AdminKey = Identity.AdminKey;
        processRuntime = new(App, resourceNames, ClusterFixtureDiagnostics.FindRepositoryRoot().FullName);
        diagnostics = new(App);
        diagnostics.Start(App.Services.GetRequiredService<ResourceLoggerService>());
        processEvidenceDirectory = await ClusterFixtureApplicationStartup.ObserveStartedAsync(app, resourceNames,
            cancellationToken).ConfigureAwait(false);
    }
}
