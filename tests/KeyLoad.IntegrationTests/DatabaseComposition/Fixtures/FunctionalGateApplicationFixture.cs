using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using KeyLoad.IntegrationTests.Features.StorageReplication;
using TUnit.Aspire;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal sealed class FunctionalGateApplicationFixture(FunctionalGateFixture owner) : AspireFixture<Projects.KeyLoad_AppHost>
{
    protected override TimeSpan ResourceTimeout => TimeSpan.FromMinutes(2);
    protected override bool DumpResourceLogsOnFailure => false;
    protected override CancellationToken RunCancellationToken => owner.ExecutionToken;
    protected override string[] Args => owner.CreateAspireArguments();

    protected override void ConfigureBuilder(IDistributedApplicationTestingBuilder builder)
    {
        ClusterFixtureComposition.ConfigureLogging(builder);
        _ = FunctionalGateProjectModel.ReadResourceNames(builder, owner.NodeNames);
        builder.Eventing.Subscribe<BeforeStartEvent>((_, _) =>
        {
            owner.OwnApplication(App);
            return Task.CompletedTask;
        });
    }

    protected override Task WaitForResourcesAsync(DistributedApplication app, CancellationToken cancellationToken)
        => owner.ObserveReadyAsync(cancellationToken);
}
