using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using KeyLoad.IntegrationTests.Features.Messaging;
using KeyLoad.IntegrationTests.Features.StorageReplication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KeyLoad.IntegrationTests;

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
        ClusterFixtureComposition.ConfigureTestOverrides(builder, commandBytes, httpAdmission, databaseLimits, queryExecution);
        queueDeadline?.Configure(builder);
        runtimeSql.Configure(builder, Root);
        RemoteTransferCoordinatorFixtureConfiguration.Configure(builder, transferCoordinator);
        resourceNames = ClusterFixtureComposition.GetResourceNames(builder);
        ClusterFixtureComposition.ConfigureLogging(builder);
        builder.Eventing.Subscribe<BeforeStartEvent>((_, _) =>
        {
            App = base.App;
            AdminKey = Identity.AdminKey;
            processRuntime = new(App, resourceNames, ClusterFixtureDiagnostics.FindRepositoryRoot().FullName);
            diagnostics = new(App);
            diagnostics.Start(App.Services.GetRequiredService<ResourceLoggerService>());
            return Task.CompletedTask;
        });
    }

    protected override async Task WaitForResourcesAsync(DistributedApplication app, CancellationToken cancellationToken)
    {
        processEvidenceDirectory = await ClusterFixtureApplicationStartup.ObserveStartedAsync(app, resourceNames,
            cancellationToken).ConfigureAwait(false);
    }
}
