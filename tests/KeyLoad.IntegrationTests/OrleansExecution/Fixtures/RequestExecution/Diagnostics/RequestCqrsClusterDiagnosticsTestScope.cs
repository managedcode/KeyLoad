using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using KeyLoad.Clustering;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.IntegrationTests.OrleansExecution.Helpers;

internal sealed class RequestCqrsClusterDiagnosticsTestScope(Guid waveId, RequestCqrsLifecycleEvidence lifecycle) : IAsyncDisposable
{
    internal DistributedApplication? application;
    private RequestCqrsClusterDiagnostics? diagnostics;
    private RequestCqrsClusterDiagnosticsScopeStreamOwner? streamOwner;
    private RequestCqrsClusterDiagnosticsIndependentConsumer? independentConsumer;
    internal ProjectResource[] resources = [];
    private readonly RequestCqrsClusterDiagnosticsSubscriberObserver subscriberObserver = new();
    private string? dataRoot;
    private string? artifactPath;
    private bool diagnosticsJoined;
    private bool applicationAcquired;
    private bool applicationDisposedSuccessfully;
    private bool cleanupJoinFailed;
    private bool dataRootOwned;

    internal Guid WaveId { get; } = waveId;
    internal RequestCqrsClusterDiagnostics Capture => diagnostics
        ?? throw new InvalidOperationException("The diagnostics owner is absent.");
    internal string OwnedArtifactPath => artifactPath
        ?? throw new InvalidOperationException("The owned artifact path is absent.");
    internal RequestCqrsClusterDiagnosticsIndependentConsumer IndependentConsumer => independentConsumer
        ?? throw new InvalidOperationException("The execution independent consumer is absent.");
    internal RequestCqrsLifecycleSnapshot ReadLifecycleSnapshot() => lifecycle.Snapshot();

    internal Task WaitForSubscriberStateAsync(bool expected)
    {
        lifecycle.SetStage(RequestCqrsLifecycleStage.SubscriberAdmission);
        return subscriberObserver.WaitForStateAsync(expected);
    }

    internal void CompleteResourceStream(string node)
        => (streamOwner ?? throw new InvalidOperationException("The execution stream owner is absent."))
            .CompleteResource(node);

    internal async Task StartAsync(CancellationToken cancellationToken, bool startIndependentConsumer = false)
    {
        lifecycle.SetTokens(cancellationToken, default, default);
        lifecycle.SetStage(RequestCqrsLifecycleStage.BuilderCreate);
        var plannedDataRoot = RequestCqrsClusterDiagnosticsArtifactFiles.DataRootPath(WaveId);
        RequestCqrsClusterDiagnosticsArtifactFiles.CreateDataRootDirectory(plannedDataRoot);
        dataRoot = plannedDataRoot;
        dataRootOwned = true;
        RequestCqrsClusterDiagnosticsArtifactFiles.RestrictDataRoot(plannedDataRoot);
        var args = RequestCqrsClusterDiagnosticsArtifactFiles.CreateAppHostArguments(plannedDataRoot);
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.KeyLoad_AppHost>(args,
            cancellationToken).ConfigureAwait(false);
        resources = RequestCqrsClusterNodeResources.Select(builder);
        lifecycle.SetStage(RequestCqrsLifecycleStage.AppHostBuild);
        var ownedApplication = await builder.BuildAsync(cancellationToken).ConfigureAwait(false);
        application = ownedApplication;
        applicationAcquired = true;
        var loggerService = ownedApplication.Services.GetRequiredService<ResourceLoggerService>();
        streamOwner = new(loggerService, resources, lifecycle);
        subscriberObserver.Initialize(loggerService, cancellationToken, lifecycle.RecordOwnerFailure);
        lifecycle.BindObserver(subscriberObserver);
        diagnostics = RequestCqrsClusterDiagnostics.Start(WaveId, resources, loggerService,
            lifecycle.RecordOwnerFailure, new(subscriberObserver));
        streamOwner.BindDiagnostics(diagnostics);
        lifecycle.BindDiagnostics(diagnostics);
        artifactPath = RequestCqrsClusterDiagnosticsArtifactFiles.ExpectedPath(WaveId);
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
        => RequestCqrsClusterDiagnosticsScenarioPublisher.EmitMalformedThenOneValidPerNode(this, lifecycle);

    internal void EmitFortyValidLinesPerNode()
        => RequestCqrsClusterDiagnosticsScenarioPublisher.EmitFortyValidLinesPerNode(this, lifecycle);

    internal async Task<byte[]> CompleteAndReadArtifactAsync(CancellationToken cancellationToken)
    {
        lifecycle.SetStage(RequestCqrsLifecycleStage.CaptureJoin);
        await JoinOriginalSubscriptionsAsync().ConfigureAwait(false);
        lifecycle.SetStage(RequestCqrsLifecycleStage.ArtifactWrite);
        return await RequestCqrsClusterDiagnosticsArtifactFiles.WriteAndReadAsync(diagnostics!, artifactPath!,
            cancellationToken).ConfigureAwait(false);
    }

    internal async Task JoinOriginalSubscriptionsAsync()
    {
        lifecycle.SetStage(RequestCqrsLifecycleStage.CaptureJoin);
        var ownedStreams = streamOwner
            ?? throw new InvalidOperationException("The execution stream owner is absent.");
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
        await DisposeDiagnosticsAsync(failures).ConfigureAwait(false);
        lifecycle.SetStage(RequestCqrsLifecycleStage.IndependentConsumerJoin);
        await DisposeIndependentConsumerAsync(failures).ConfigureAwait(false);
        lifecycle.SetStage(RequestCqrsLifecycleStage.ObserverJoin);
        await RequestCqrsClusterDiagnosticsObserverCleanup.DisposeAsync(subscriberObserver, failures,
                lifecycle.RecordOwnerFailure)
            .ConfigureAwait(false);
        lifecycle.SetStage(RequestCqrsLifecycleStage.AppHostDispose);
        await DisposeApplicationAsync(failures).ConfigureAwait(false);
        lifecycle.SetStage(RequestCqrsLifecycleStage.ArtifactWrite);
        var artifactFailureCount = failures.Count;
        RequestCqrsClusterDiagnosticsArtifactFiles.DeleteOwned(artifactPath, failures);
        if (failures.Count > artifactFailureCount)
        {
            lifecycle.RecordOwnerFailure(RequestCqrsLifecycleStage.ArtifactWrite);
        }
        cleanupJoinFailed |= failures.Count != 0;
        lifecycle.SetStage(RequestCqrsLifecycleStage.OwnedRootCleanup);
        if (RequestCqrsClusterDiagnosticsRootGuard.CanDelete(dataRootOwned, dataRoot,
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
        {
            return;
        }
        var ownedConsumer = independentConsumer;
        try
        {
            await ownedConsumer.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error)) { RequestCqrsLifecycleFailureObserver.Append(failures, error, lifecycle.RecordOwnerFailure, RequestCqrsLifecycleStage.IndependentJoin); }
        catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error)) { RequestCqrsLifecycleFailureObserver.Append(failures, error, lifecycle.RecordOwnerFailure, RequestCqrsLifecycleStage.IndependentJoin); }
        await ownedConsumer.RetryFailedCloseAsync(failures, lifecycle.RecordOwnerFailure).ConfigureAwait(false);
        if (ownedConsumer.IsJoined)
        {
            independentConsumer = null;
        }
        else
        {
            RequestCqrsLifecycleFailureObserver.Append(failures,
            new InvalidOperationException("The independent execution consumer did not fully join."),
            lifecycle.RecordOwnerFailure, RequestCqrsLifecycleStage.IndependentJoin);
        }
    }

    private async Task DisposeDiagnosticsAsync(List<Exception> failures)
    {
        if (diagnosticsJoined || diagnostics is null)
        {
            return;
        }
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
    {
        await diagnostics!.DisposeAsync().ConfigureAwait(false);
    }

    private async Task DisposeApplicationAsync(List<Exception> failures)
    {
        var ownedApplication = application;
        if (ownedApplication is null)
        {
            return;
        }
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
        {
            cleanupJoinFailed = true;
        }
    }

}
