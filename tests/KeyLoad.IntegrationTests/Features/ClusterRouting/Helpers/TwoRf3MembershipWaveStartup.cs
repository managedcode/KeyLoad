using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TwoRf3MembershipWaveStartup
{
    internal static async Task StartCoreAsync(TwoRf3MembershipWave wave, CancellationToken cancellationToken)
    {
        var firstStart = wave.dataRoot is null;
        var root = wave.dataRoot ?? wave.CreateOwnedRoot();
        if (firstStart)
        { wave.Profile = (await NodeEpochRf3Profile.CreatePriorAsync(root, cancellationToken).ConfigureAwait(false)).Profile; }
        var repository = ClusterFixtureDiagnostics.FindRepositoryRoot().FullName;
        string? githubReference = null;
        if (wave.localImageSelection is null)
        {
            githubReference = await ClusterFixtureImageIdentity.ReadVerifiedReferenceAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            _ = await LocalRf3ImageIdentity.ReadVerifiedAsync(repository, wave.localImageSelection, cancellationToken)
                .ConfigureAwait(false) ?? throw new InvalidOperationException(TwoRf3MembershipProtocol.ImageMismatch);
        }
        if (wave.queryProbe && wave.queryControls is null)
        { wave.queryControls = RequestCqrsProbeFixture.Create(root, Guid.NewGuid()); }
        if (wave.nativeDiscoveryOmissionSelected && wave.nativeDiscoveryOmission is null)
        { wave.nativeDiscoveryOmission = KeyLoad.IntegrationTests.Features.StorageRecovery.NativeCapabilityOmissionRf3Fixture.Create(root, cancellationToken); }
        var args = TwoRf3WaveArguments.Create(root, wave.localImageSelection, wave.registerPhysicalOwners, wave.remoteDocumentReads, wave.remotePartitionQueries, wave.queryControls, wave.protectedDocuments, wave.movementMaxBatchBytes, wave.movementMaxFrameBytes, wave.frameObservation, wave.nativeDiscoveryOmission);
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.KeyLoad_AppHost>(args,
            (_, settings) => wave.capacity.BindOriginalParameters(settings), cancellationToken).ConfigureAwait(false);
        await wave.capacity.RequireOriginalParametersAsync(builder.Resources.OfType<ParameterResource>(), cancellationToken).ConfigureAwait(false);
        wave.capacity.RestoreOriginalEndpoints(builder.Resources.OfType<ContainerResource>());
        if (wave.protectedDocuments)
        {
            wave.MovementTargetPeerKey = await builder.Resources.OfType<ParameterResource>()
                .Single(resource => resource.Name == "membership-peer-b").GetValueAsync(cancellationToken).ConfigureAwait(false);
        }
        var containerNames = TwoRf3MembershipContainerNames.Read(builder.Resources.OfType<ContainerResource>());
        if (wave.activationIsolation)
        {
            wave.IsolationOwner = await ReplicaIsolationOwner.PrepareSixAsync(builder, root, repository,
            containerNames, wave, cancellationToken).ConfigureAwait(false);
        }
        wave.application = await builder.BuildAsync(cancellationToken).ConfigureAwait(false);
        wave.applicationDisposed = false;
        wave.nodeLocksReleased = false;
        if (wave.remoteDocumentReads)
        { wave.RemoteRuntimeOwner = new ContainerRuntimeControl(wave.application, containerNames, repository); }
        if (wave.IsolationOwner is { } isolation)
        { await isolation.VerifyBeforeStartAsync(wave.application, cancellationToken).ConfigureAwait(false); }
        else if (wave.localImageSelection is null)
        {
            await TwoRf3MembershipImageAssertions.VerifyAsync(wave.application,
                    githubReference ?? throw new InvalidOperationException(TwoRf3MembershipProtocol.ImageMismatch),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            wave.localImageIdentity = await LocalRf3ImageIdentity.VerifyBeforeStartAsync(wave.application, repository,
                wave.localImageSelection, TwoRf3MembershipProtocol.Nodes, cancellationToken).ConfigureAwait(false);
        }
        await StartAndVerifyReadyAsync(wave, wave.application, containerNames, cancellationToken).ConfigureAwait(false);
    }

    private static async Task StartAndVerifyReadyAsync(TwoRf3MembershipWave wave,
        DistributedApplication application, Dictionary<string, string> containerNames, CancellationToken cancellationToken)
    {
        wave.startAttempted = true;
        await application.StartAsync(cancellationToken).ConfigureAwait(false);
        await wave.WaitForSixHealthyAsync(cancellationToken).ConfigureAwait(false);
        if (wave.IsolationOwner is { } startedIsolation)
        { await startedIsolation.VerifyStartedAsync(application, cancellationToken).ConfigureAwait(false); }
        wave.capacity.CaptureOriginalEndpoints(wave);
        if (wave.remoteDocumentReads)
        {
            await TwoRf3MembershipDataReadiness.WaitAsync(application,
                application.Services.GetRequiredService<IOptions<TestExecutionOptions>>(),
                application.Services.GetRequiredService<TimeProvider>(), cancellationToken).ConfigureAwait(false);
        }
        if (wave.localImageIdentity is { } identity)
        {
            await LocalRf3ImageIdentity.VerifyStartedContainersAsync(identity, containerNames,
                TwoRf3MembershipProtocol.Nodes, cancellationToken).ConfigureAwait(false);
        }
    }
}
