using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using KeyLoad.Orleans;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting.Helpers;

internal sealed class RequestCqrsRf3DiagnosticsTestScope(Guid waveId) : IAsyncDisposable
{
    private readonly HashSet<string> completedResourceNames = new(StringComparer.Ordinal);
    private DistributedApplication? application;
    private RequestCqrsRf3Diagnostics? diagnostics;
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

    internal async Task StartAsync(CancellationToken cancellationToken, bool startIndependentConsumer = false)
    {
        var plannedDataRoot = RequestCqrsRf3DiagnosticsArtifactFiles.DataRootPath(WaveId);
        RequestCqrsRf3DiagnosticsArtifactFiles.CreateDataRootDirectory(plannedDataRoot);
        dataRoot = plannedDataRoot;
        dataRootOwned = true;
        RequestCqrsRf3DiagnosticsArtifactFiles.RestrictDataRoot(plannedDataRoot);
        var args = RequestCqrsRf3DiagnosticsArtifactFiles.CreateAppHostArguments(plannedDataRoot);
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.KeyLoad_AppHost>(args,
            cancellationToken).ConfigureAwait(false);
        resources = SelectNodeResources(builder);
        var ownedApplication = await builder.BuildAsync(cancellationToken).ConfigureAwait(false);
        application = ownedApplication;
        applicationAcquired = true;
        var loggerService = ownedApplication.Services.GetRequiredService<ResourceLoggerService>();
        subscriberObserver.Initialize(loggerService, cancellationToken);
        diagnostics = RequestCqrsRf3Diagnostics.Start(WaveId, resources, loggerService);
        artifactPath = RequestCqrsRf3DiagnosticsArtifactFiles.ExpectedPath(WaveId);
        if (startIndependentConsumer)
        {
            var resource = builder.Resources.OfType<ParameterResource>()
                .Single(candidate => candidate.Name == "admin-key");
            independentConsumer = new(loggerService, resource, cancellationToken);
            await independentConsumer.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        await subscriberObserver.WaitForStateAsync(true).ConfigureAwait(false);
    }

    internal void EmitMalformedThenOneValidPerNode()
    {
        foreach (var resource in resources)
        {
            var logger = application!.Services.GetRequiredService<ResourceLoggerService>().GetLogger(resource);
            RequestCqrsRf3DiagnosticsTestPublisher.EmitMalformed(logger);
            RequestCqrsRf3DiagnosticsTestPublisher.EmitValid(logger);
        }
    }

    internal void EmitFortyValidLinesPerNode()
    {
        foreach (var resource in resources)
        {
            var logger = application!.Services.GetRequiredService<ResourceLoggerService>().GetLogger(resource);
            RequestCqrsRf3DiagnosticsTestPublisher.EmitFortyValid(logger);
        }
    }

    internal async Task<byte[]> CompleteAndReadArtifactAsync(CancellationToken cancellationToken)
    {
        await JoinOriginalSubscriptionsAsync().ConfigureAwait(false);
        return await RequestCqrsRf3DiagnosticsArtifactFiles.WriteAndReadAsync(diagnostics!, artifactPath!,
            cancellationToken).ConfigureAwait(false);
    }

    internal async Task JoinOriginalSubscriptionsAsync()
    {
        CompleteResourceStreams();
        await JoinDiagnosticsTwiceAsync().ConfigureAwait(false);
        await subscriberObserver.DisposeAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        CompleteResourceStreams(failures);
        if (independentConsumer is not null)
        {
            var ownedConsumer = independentConsumer;
            try
            { await independentConsumer.DisposeAsync().ConfigureAwait(false); }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
            { failures.Add(error); }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
            { failures.Add(error); }
            await ownedConsumer.RetryFailedCloseAsync(failures).ConfigureAwait(false);
            if (ownedConsumer.IsJoined)
            { independentConsumer = null; }
            else
            { failures.Add(new InvalidOperationException("The independent native consumer did not fully join.")); }
        }
        if (!diagnosticsJoined && diagnostics is not null)
        {
            try
            {
                await diagnostics.DisposeAsync().ConfigureAwait(false);
                diagnosticsJoined = true;
                diagnostics = null;
            }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
            { failures.Add(error); }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
            { failures.Add(error); }
        }
        await DisposeSubscriberObserverAsync(failures).ConfigureAwait(false);
        await DisposeApplicationAsync(failures).ConfigureAwait(false);
        RequestCqrsRf3DiagnosticsArtifactFiles.DeleteOwned(artifactPath, failures);
        cleanupJoinFailed |= failures.Count != 0;
        if (CanDeleteDataRoot(failures))
        {
            var ownedRoot = dataRoot!;
            ServerFailureObserver.Observe(() => Directory.Delete(ownedRoot, recursive: true), failures);
            if (failures.Count == 0)
            {
                dataRoot = null;
                dataRootOwned = false;
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private async Task DisposeSubscriberObserverAsync(List<Exception> failures)
    {
        try
        { await subscriberObserver.DisposeAsync().ConfigureAwait(false); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        { failures.Add(error); }
        await subscriberObserver.RetryFailedCloseAsync(failures).ConfigureAwait(false);
    }

    private async Task DisposeApplicationAsync(List<Exception> failures)
    {
        var ownedApplication = application;
        if (ownedApplication is null)
        { return; }
        var before = failures.Count;
        await ServerFailureObserver.ObserveAsync(() => ownedApplication.DisposeAsync().AsTask(), failures)
            .ConfigureAwait(false);
        if (failures.Count == before)
        {
            application = null;
            applicationDisposedSuccessfully = true;
        }
        else
        { cleanupJoinFailed = true; }
    }

    private bool CanDeleteDataRoot(List<Exception> failures)
        => dataRootOwned && dataRoot is not null && DiagnosticsSettled && IndependentConsumerSettled
            && subscriberObserver.IsJoined && ApplicationSettled && !cleanupJoinFailed
            && failures.Count == 0 && Directory.Exists(dataRoot);

    private bool DiagnosticsSettled => diagnostics is null || diagnosticsJoined;
    private bool IndependentConsumerSettled => independentConsumer is null || independentConsumer.IsJoined;
    private bool ApplicationSettled => !applicationAcquired || applicationDisposedSuccessfully;

    private void CompleteResourceStreams()
    {
        var failures = new List<Exception>();
        CompleteResourceStreams(failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private void CompleteResourceStreams(List<Exception> failures)
    {
        var logger = application?.Services.GetService<ResourceLoggerService>();
        if (logger is null)
        { return; }
        foreach (var resource in resources)
        {
            if (completedResourceNames.Contains(resource.Name))
            { continue; }
            var before = failures.Count;
            ServerFailureObserver.Observe(() => logger.Complete(resource), failures);
            if (failures.Count == before)
            { completedResourceNames.Add(resource.Name); }
        }
    }

    private async Task JoinDiagnosticsTwiceAsync()
    {
        var capture = diagnostics ?? throw new InvalidOperationException("The Aspire diagnostics owner is absent.");
        var first = capture.DisposeAsync().AsTask();
        var repeated = capture.DisposeAsync().AsTask();
        if (!ReferenceEquals(first, repeated))
        { throw new InvalidOperationException("Repeated diagnostics disposal did not share one completion task."); }
        await first.ConfigureAwait(false);
        diagnosticsJoined = true;
    }

    private static ContainerResource[] SelectNodeResources(IDistributedApplicationTestingBuilder builder)
    {
        var selected = builder.Resources.OfType<ContainerResource>().Where(resource => IsNode(resource.Name))
            .OrderBy(resource => resource.Name, StringComparer.Ordinal).ToArray();
        if (selected.Length != RequestCqrsRf3Protocol.NodeCount)
        { throw new InvalidOperationException("The actual Aspire model did not contain three C1 node containers."); }
        return selected;
    }

    private static bool IsNode(string name) => name is RequestCqrsRf3Protocol.Node1
        or RequestCqrsRf3Protocol.Node2 or RequestCqrsRf3Protocol.Node3;

}
