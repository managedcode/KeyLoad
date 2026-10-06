using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using KeyLoad.Orleans;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting.Helpers;

internal sealed class RequestCqrsRf3DiagnosticsTestScope(Guid waveId, RequestCqrsLifecycleEvidence lifecycle) : IAsyncDisposable
{
    private DistributedApplication? application;
    private RequestCqrsRf3Diagnostics? diagnostics;
    private RequestCqrsRf3DiagnosticsScopeStreamOwner? streamOwner;
    private RequestCqrsRf3DiagnosticsIndependentConsumer? independentConsumer;
    private ContainerResource[] resources = [];
    private readonly RequestCqrsRf3DiagnosticsSubscriberObserver subscriberObserver = new();
    private string? dataRoot;
    private string? artifactPath;
    private bool diagnosticsJoined;
    private bool applicationAcquired;
    private bool applicationDisposedSuccessfully;
    private bool cleanupJoinFailed;
    private bool dataRootOwned;

    internal Guid WaveId { get; } = waveId;
    internal RequestCqrsRf3Diagnostics Capture => diagnostics
        ?? throw new InvalidOperationException("The diagnostics owner is absent.");
    internal string OwnedArtifactPath => artifactPath
        ?? throw new InvalidOperationException("The owned artifact path is absent.");
    internal RequestCqrsRf3DiagnosticsIndependentConsumer IndependentConsumer => independentConsumer
        ?? throw new InvalidOperationException("The native independent consumer is absent.");

    internal Task WaitForSubscriberStateAsync(bool expected)
    {
        lifecycle.SetStage(RequestCqrsLifecycleStage.SubscriberAdmission);
        return subscriberObserver.WaitForStateAsync(expected);
    }

    internal void CompleteResourceStream(string node)
        => (streamOwner ?? throw new InvalidOperationException("The native stream owner is absent."))
            .CompleteResource(node);

    internal async Task StartAsync(CancellationToken cancellationToken, bool startIndependentConsumer = false)
    {
        lifecycle.SetTokens(cancellationToken, default, default);
        lifecycle.SetStage(RequestCqrsLifecycleStage.BuilderCreate);
        var plannedDataRoot = RequestCqrsRf3DiagnosticsArtifactFiles.DataRootPath(WaveId);
        RequestCqrsRf3DiagnosticsArtifactFiles.CreateDataRootDirectory(plannedDataRoot);
        dataRoot = plannedDataRoot;
        dataRootOwned = true;
        RequestCqrsRf3DiagnosticsArtifactFiles.RestrictDataRoot(plannedDataRoot);
        var args = RequestCqrsRf3DiagnosticsArtifactFiles.CreateAppHostArguments(plannedDataRoot);
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.KeyLoad_AppHost>(args,
            cancellationToken).ConfigureAwait(false);
        resources = RequestCqrsRf3NodeResources.Select(builder);
        lifecycle.SetStage(RequestCqrsLifecycleStage.AppHostBuild);
        var ownedApplication = await builder.BuildAsync(cancellationToken).ConfigureAwait(false);
        application = ownedApplication;
        applicationAcquired = true;
        var loggerService = ownedApplication.Services.GetRequiredService<ResourceLoggerService>();
        streamOwner = new(loggerService, resources, lifecycle);
        subscriberObserver.Initialize(loggerService, cancellationToken, lifecycle.RecordOwnerFailure);
        lifecycle.BindObserver(subscriberObserver);
        diagnostics = RequestCqrsRf3Diagnostics.Start(WaveId, resources, loggerService,
            lifecycle.RecordOwnerFailure);
        streamOwner.BindDiagnostics(diagnostics);
        lifecycle.BindDiagnostics(diagnostics);
        artifactPath = RequestCqrsRf3DiagnosticsArtifactFiles.ExpectedPath(WaveId);
        if (startIndependentConsumer)
        {
            var resource = builder.Resources.OfType<ParameterResource>()
                .Single(candidate => candidate.Name == "admin-key");
            independentConsumer = new(loggerService, resource, cancellationToken,
                lifecycle.RecordOwnerFailure);
            lifecycle.BindConsumer(independentConsumer);
            await independentConsumer.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        lifecycle.SetStage(RequestCqrsLifecycleStage.SubscriberAdmission);
        await subscriberObserver.WaitForStateAsync(true).ConfigureAwait(false);
    }

    internal void EmitMalformedThenOneValidPerNode()
    {
        lifecycle.SetStage(RequestCqrsLifecycleStage.Scenario);
        foreach (var resource in resources)
        {
            var logger = application!.Services.GetRequiredService<ResourceLoggerService>().GetLogger(resource);
            RequestCqrsRf3DiagnosticsTestPublisher.EmitMalformed(logger);
            RequestCqrsRf3DiagnosticsTestPublisher.EmitValid(logger);
        }
    }

    internal void EmitFortyValidLinesPerNode()
    {
        lifecycle.SetStage(RequestCqrsLifecycleStage.Scenario);
        foreach (var resource in resources)
        {
            var logger = application!.Services.GetRequiredService<ResourceLoggerService>().GetLogger(resource);
            RequestCqrsRf3DiagnosticsTestPublisher.EmitFortyValid(logger);
        }
    }

    internal async Task<byte[]> CompleteAndReadArtifactAsync(CancellationToken cancellationToken)
    {
        lifecycle.SetStage(RequestCqrsLifecycleStage.CaptureJoin);
        await JoinOriginalSubscriptionsAsync().ConfigureAwait(false);
        lifecycle.SetStage(RequestCqrsLifecycleStage.ArtifactWrite);
        return await RequestCqrsRf3DiagnosticsArtifactFiles.WriteAndReadAsync(diagnostics!, artifactPath!,
            cancellationToken).ConfigureAwait(false);
    }

    internal async Task JoinOriginalSubscriptionsAsync()
    {
        lifecycle.SetStage(RequestCqrsLifecycleStage.CaptureJoin);
        var ownedStreams = streamOwner
            ?? throw new InvalidOperationException("The native stream owner is absent.");
        ownedStreams.CompleteResourceStreams();
        await ownedStreams.JoinDiagnosticsTwiceAsync().ConfigureAwait(false);
        lifecycle.SetStage(RequestCqrsLifecycleStage.ObserverJoin);
        await subscriberObserver.DisposeAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        lifecycle.SetStage(RequestCqrsLifecycleStage.CaptureJoin);
        streamOwner?.CompleteResourceStreams(failures);
        lifecycle.SetStage(RequestCqrsLifecycleStage.IndependentConsumerJoin);
        await DisposeIndependentConsumerAsync(failures).ConfigureAwait(false);
        await DisposeDiagnosticsAsync(failures).ConfigureAwait(false);
        lifecycle.SetStage(RequestCqrsLifecycleStage.ObserverJoin);
        await RequestCqrsRf3DiagnosticsObserverCleanup.DisposeAsync(subscriberObserver, failures,
                lifecycle.RecordOwnerFailure)
            .ConfigureAwait(false);
        lifecycle.SetStage(RequestCqrsLifecycleStage.AppHostDispose);
        await DisposeApplicationAsync(failures).ConfigureAwait(false);
        lifecycle.SetStage(RequestCqrsLifecycleStage.ArtifactWrite);
        var artifactFailureCount = failures.Count;
        RequestCqrsRf3DiagnosticsArtifactFiles.DeleteOwned(artifactPath, failures);
        if (failures.Count > artifactFailureCount)
        { lifecycle.RecordOwnerFailure(RequestCqrsLifecycleStage.ArtifactWrite); }
        cleanupJoinFailed |= failures.Count != 0;
        lifecycle.SetStage(RequestCqrsLifecycleStage.OwnedRootCleanup);
        if (RequestCqrsRf3DiagnosticsRootGuard.CanDelete(dataRootOwned, dataRoot,
            diagnostics is null || diagnosticsJoined, independentConsumer is null || independentConsumer.IsJoined,
            subscriberObserver.IsJoined, !applicationAcquired || applicationDisposedSuccessfully,
            cleanupJoinFailed, failures.Count != 0))
        {
            var ownedRoot = dataRoot!;
            RequestCqrsLifecycleFailureObserver.Observe(() => Directory.Delete(ownedRoot, recursive: true),
                failures, lifecycle.RecordOwnerFailure, RequestCqrsLifecycleStage.OwnedRootCleanup);
            if (failures.Count == 0)
            {
                dataRoot = null;
                dataRootOwned = false;
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private async Task DisposeIndependentConsumerAsync(List<Exception> failures)
    {
        if (independentConsumer is null)
        { return; }
        var ownedConsumer = independentConsumer;
        try
        { await ownedConsumer.DisposeAsync().ConfigureAwait(false); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { RequestCqrsLifecycleFailureObserver.Append(failures, error, lifecycle.RecordOwnerFailure, RequestCqrsLifecycleStage.IndependentJoin); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { RequestCqrsLifecycleFailureObserver.Append(failures, error, lifecycle.RecordOwnerFailure, RequestCqrsLifecycleStage.IndependentJoin); }
        await ownedConsumer.RetryFailedCloseAsync(failures, lifecycle.RecordOwnerFailure).ConfigureAwait(false);
        if (ownedConsumer.IsJoined)
        { independentConsumer = null; }
        else
        {
            RequestCqrsLifecycleFailureObserver.Append(failures,
            new InvalidOperationException("The independent native consumer did not fully join."),
            lifecycle.RecordOwnerFailure, RequestCqrsLifecycleStage.IndependentJoin);
        }
    }

    private async Task DisposeDiagnosticsAsync(List<Exception> failures)
    {
        if (diagnosticsJoined || diagnostics is null)
        { return; }
        var before = failures.Count;
        var original = DisposeOwnedDiagnosticsAsync();
        await RequestCqrsLifecycleFailureObserver.ObserveAsync(
            () => original, failures, lifecycle.RecordOwnerFailure,
            RequestCqrsLifecycleStage.CaptureDrain).ConfigureAwait(false);
        if (failures.Count == before)
        {
            diagnosticsJoined = true;
            diagnostics = null;
        }
    }

    private async Task DisposeOwnedDiagnosticsAsync()
    { await diagnostics!.DisposeAsync().ConfigureAwait(false); }

    private async Task DisposeApplicationAsync(List<Exception> failures)
    {
        var ownedApplication = application;
        if (ownedApplication is null)
        { return; }
        var before = failures.Count;
        await RequestCqrsLifecycleFailureObserver.ObserveAsync(
            () => ownedApplication.DisposeAsync().AsTask(), failures, lifecycle.RecordOwnerFailure,
            RequestCqrsLifecycleStage.AppHostDispose).ConfigureAwait(false);
        if (failures.Count == before)
        {
            application = null;
            applicationDisposedSuccessfully = true;
        }
        else
        { cleanupJoinFailed = true; }
    }

}
