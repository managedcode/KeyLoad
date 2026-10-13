using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TwoRf3MembershipWaveStartup
{
    internal static async Task<TwoRf3MembershipWave> StartOwnedAsync(TwoRf3MembershipWave wave, CancellationToken cancellationToken, Action<string>? observe = null)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => StartCoreAsync(wave, cancellationToken, observe), failures)
            .ConfigureAwait(false);
        if (failures.Count == 0)
        { return wave; }
        if (wave.startupCallbacks is { } capture)
        { ServerFailureObserver.Observe(() => capture.Failed(failures[0], cancellationToken.IsCancellationRequested), failures); }
        await ServerFailureObserver.ObserveAsync(() => wave.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        throw new InvalidOperationException(TwoRf3MembershipProtocol.MissingState);
    }

    internal static async Task StartCoreAsync(TwoRf3MembershipWave wave, CancellationToken cancellationToken, Action<string>? observe = null)
    {
        var firstStart = wave.dataRoot is null;
        var root = wave.dataRoot ?? wave.CreateOwnedRoot();
        if (firstStart)
        { observe?.Invoke("ProfileCreate"); wave.Profile = (await NodeEpochRf3Profile.CreatePriorAsync(root, cancellationToken).ConfigureAwait(false)).Profile; }
        var repository = ClusterFixtureDiagnostics.FindRepositoryRoot().FullName;
        var githubReference = await TwoRf3MembershipImageAdmission.ReadReferenceAsync(wave, repository,
            observe, cancellationToken).ConfigureAwait(false);
        NativeMembershipDiagnosticWaveLifecycle.PrepareControls(wave, root, cancellationToken);
        await NativeCanonicalModelCutWaveStartup.PrepareAsync(wave, root).ConfigureAwait(false);
        NativeBlobWireWaveLifecycle.Prepare(wave, root, cancellationToken);
        var args = TwoRf3WaveArguments.Create(root, wave.localImageSelection, wave.registerPhysicalOwners, wave.remoteDocumentReads, wave.remotePartitionQueries, wave.queryControls, wave.protectedDocuments, wave.movementMaxBatchBytes, wave.movementMaxFrameBytes, wave.frameObservation, wave.nativeDiscoveryOmission, wave.EventVectorReservedProbe, wave.nativeMembershipControls, wave.TenantCapacityPlacement);
        args = NativeBlobWireWaveLifecycle.Arguments(wave, args);
        observe?.Invoke("BuilderCreate");
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.KeyLoad_AppHost>(args,
            (_, settings) => wave.capacity.BindOriginalParameters(settings), cancellationToken).ConfigureAwait(false);
        observe?.Invoke("OriginalParameters");
        await wave.capacity.RequireOriginalParametersAsync(builder.Resources.OfType<ParameterResource>(), cancellationToken).ConfigureAwait(false);
        await NativeBlobWireWaveLifecycle.PublishOwnerAsync(wave, builder.Resources.OfType<ParameterResource>(), cancellationToken).ConfigureAwait(false);
        wave.capacity.RestoreOriginalEndpoints(builder.Resources.OfType<ContainerResource>());
        wave.canonicalCutControls?.Configure(builder);
        KeyLoad.IntegrationTests.Features.Search.RemoteAnnPublicationRf3Enrollment.Configure(builder, wave.AnnReadExecution);
        KeyLoad.IntegrationTests.Features.Messaging.RemoteTransferRf3Enrollment.Configure(builder,
            wave.remoteTransferPrincipalId, cancellationToken);
        if (wave.protectedDocuments)
        {
            observe?.Invoke("OriginalPeerKey");
            wave.MovementTargetPeerKey = await builder.Resources.OfType<ParameterResource>()
                .Single(resource => resource.Name == "membership-peer-b").GetValueAsync(cancellationToken).ConfigureAwait(false);
        }
        var containerNames = TwoRf3MembershipContainerNames.Read(builder.Resources.OfType<ContainerResource>());
        if (wave.activationIsolation)
        {
            observe?.Invoke("IsolationPrepare");
            wave.IsolationOwner = await ReplicaIsolationOwner.PrepareSixAsync(builder, root, repository,
            containerNames, wave, cancellationToken).ConfigureAwait(false);
        }
        observe?.Invoke("BuildApplication");
        wave.application = await builder.BuildAsync(cancellationToken).ConfigureAwait(false);
        wave.applicationDisposed = false;
        wave.nodeLocksReleased = false;
        if (wave.remoteDocumentReads)
        {
            wave.RemoteRuntimeOwner = new ContainerRuntimeControl(wave.application, containerNames, repository,
            wave.localImageSelection);
        }
        observe?.Invoke("VerifyImageBeforeStart");
        await VerifyBeforeStartAsync(wave, wave.application, repository, githubReference, cancellationToken)
            .ConfigureAwait(false);
        wave.startupCallbacks?.Attach(wave.application, builder.Resources.OfType<ContainerResource>()
            .Where(resource => TwoRf3MembershipProtocol.Nodes.Contains(resource.Name, StringComparer.Ordinal)).ToArray());
        await StartAndVerifyReadyAsync(wave, wave.application, containerNames, cancellationToken, observe).ConfigureAwait(false);
        if (wave.startupCallbacks is { } capture)
        { observe?.Invoke("StartupReady"); await capture.Ready().ConfigureAwait(false); }
    }

    private static async Task VerifyBeforeStartAsync(TwoRf3MembershipWave wave,
        DistributedApplication application, string repository, string? githubReference,
        CancellationToken cancellationToken)
    {
        if (wave.IsolationOwner is { } isolation)
        { await isolation.VerifyBeforeStartAsync(application, cancellationToken).ConfigureAwait(false); }
        else if (wave.localImageSelection is null)
        {
            await TwoRf3MembershipImageAssertions.VerifyAsync(application,
                    githubReference ?? throw new InvalidOperationException(TwoRf3MembershipProtocol.ImageMismatch),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            wave.localImageIdentity = await LocalRf3ImageIdentity.VerifyBeforeStartAsync(application, repository,
                wave.localImageSelection, TwoRf3MembershipProtocol.Nodes, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task StartAndVerifyReadyAsync(TwoRf3MembershipWave wave,
        DistributedApplication application, Dictionary<string, string> containerNames, CancellationToken cancellationToken, Action<string>? observe = null)
    {
        wave.startAttempted = true;
        observe?.Invoke("ApplicationStart");
        await application.StartAsync(cancellationToken).ConfigureAwait(false);
        observe?.Invoke("SixHealthy");
        await wave.WaitForSixHealthyAsync(cancellationToken).ConfigureAwait(false);
        if (wave.IsolationOwner is { } startedIsolation)
        { await startedIsolation.VerifyStartedAsync(application, cancellationToken).ConfigureAwait(false); }
        wave.capacity.CaptureOriginalEndpoints(wave);
        if (wave.remoteDocumentReads)
        {
            observe?.Invoke("DataReadiness");
            await TwoRf3MembershipDataReadiness.WaitAsync(application,
                application.Services.GetRequiredService<IOptions<TestExecutionOptions>>(),
                application.Services.GetRequiredService<TimeProvider>(), cancellationToken).ConfigureAwait(false);
        }
        if (wave.localImageIdentity is { } identity)
        {
            observe?.Invoke("VerifyStartedImages");
            await LocalRf3ImageIdentity.VerifyStartedContainersAsync(identity, containerNames,
                TwoRf3MembershipProtocol.Nodes, cancellationToken).ConfigureAwait(false);
        }
    }
}
