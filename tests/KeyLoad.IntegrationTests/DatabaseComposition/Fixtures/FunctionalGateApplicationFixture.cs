using Aspire.Hosting;
using Aspire.Hosting.Testing;
using KeyLoad.IntegrationTests.StorageReplication;
using TUnit.Aspire;

namespace KeyLoad.IntegrationTests.DatabaseComposition;

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
    }

    // The testing factory runs before-start hooks concurrently with the build returning, so the application is owned
    // here, where it is passed in.
    protected override Task WaitForResourcesAsync(DistributedApplication app, CancellationToken cancellationToken)
    {
        owner.OwnApplication(app);
        return owner.ObserveReadyAsync(cancellationToken);
    }
}
