using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.ClusterRouting.Helpers;
using KeyLoad.Orleans;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed class RequestCqrsRf3WaveStartup(string dataRoot, IReadOnlyDictionary<string, string> images,
    bool configureCohort, bool requireHealthy, string? snapshotThresholdArgument, Guid diagnosticsWaveId,
    RequestCqrsProbeFixture? controls, CancellationToken cancellationToken) : IAsyncDisposable
{
    private const string MissingWaveMessage = "The C1 Aspire wave did not transfer its owned resources.";
    private readonly string[] args = CreateArguments(dataRoot, images, configureCohort, snapshotThresholdArgument, controls);
    private DistributedApplication? application;
    private RequestCqrsRf3Diagnostics? diagnostics;
    private RequestCqrsRf3DiagnosticsSubscriberObserver? subscriberObserver;
    private RequestCqrsRf3Wave? wave;

    internal static async Task<RequestCqrsRf3Wave> StartAsync(string dataRoot,
        IReadOnlyDictionary<string, string> images, bool configureCohort, bool requireHealthy,
        string? snapshotThresholdArgument, Guid diagnosticsWaveId, CancellationToken cancellationToken,
        RequestCqrsProbeFixture? controls = null)
    {
        await using var startup = new RequestCqrsRf3WaveStartup(dataRoot, images, configureCohort,
            requireHealthy, snapshotThresholdArgument, diagnosticsWaveId, controls, cancellationToken);
        return await startup.RunAsync().ConfigureAwait(false);
    }

    private async Task<RequestCqrsRf3Wave> RunAsync()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(RequestCqrsRf3Protocol.WaveDeadline);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => StartCoreAsync(deadline.Token), failures)
            .ConfigureAwait(false);
        if (failures.Count == 0 && wave is { } startedWave)
        {
            wave = null;
            return startedWave;
        }
        if (failures.Count == 0)
        { failures.Add(new InvalidOperationException(MissingWaveMessage)); }
        await ServerFailureObserver.ObserveAsync(() => DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        throw new InvalidOperationException(MissingWaveMessage);
    }

    private async Task StartCoreAsync(CancellationToken cancellationToken)
    {
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.KeyLoad_AppHost>(args,
            cancellationToken).ConfigureAwait(false);
        builder.Services.AddLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddConsole();
            logging.SetMinimumLevel(LogLevel.Warning);
        });
        var containers = ContainerNames(builder);
        var nodeResources = NodeResources(builder);
        application = await builder.BuildAsync(cancellationToken).ConfigureAwait(false);
        var loggerService = application.Services.GetRequiredService<ResourceLoggerService>();
        subscriberObserver = new RequestCqrsRf3DiagnosticsSubscriberObserver();
        subscriberObserver.Initialize(loggerService, cancellationToken);
        diagnostics = RequestCqrsRf3Diagnostics.Start(diagnosticsWaveId, nodeResources, loggerService);
        await subscriberObserver.WaitForStateAsync(true).ConfigureAwait(false);
        var observerFailures = new List<Exception>();
        await DisposeSubscriberObserverAsync(observerFailures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(observerFailures);
        await RequestCqrsRf3ImageProof.VerifyModelAsync(application, images, cancellationToken)
            .ConfigureAwait(false);
        await application.StartAsync(cancellationToken).ConfigureAwait(false);
        await WaitForNodesAsync(application, requireHealthy, cancellationToken).ConfigureAwait(false);
        var runtime = new ContainerRuntimeControl(application, containers,
            ClusterFixtureDiagnostics.FindRepositoryRoot().FullName);
        wave = RequestCqrsRf3Wave.TransferOwned(dataRoot, runtime, ref application, ref diagnostics);
    }

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        if (subscriberObserver is not null)
        {
            try
            { await subscriberObserver.DisposeAsync().ConfigureAwait(false); }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
            { failures.Add(error); }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
            { failures.Add(error); }
        }
        await ServerFailureObserver.ObserveAsync(() => JoinSubscriberObserverAsync(failures), failures)
            .ConfigureAwait(false);
        if (wave is not null)
        {
            try
            { await wave.DisposeAsync().ConfigureAwait(false); }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
            { failures.Add(error); }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
            { failures.Add(error); }
            wave = null;
        }
        if (application is not null)
        {
            await ServerFailureObserver.ObserveAsync(
                () => RequestCqrsRf3Wave.CompleteAsync(application, diagnostics, failures), failures)
                .ConfigureAwait(false);
            application = null;
        }
        if (diagnostics is not null)
        {
            try
            { await diagnostics.DisposeAsync().ConfigureAwait(false); }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
            { RecordDiagnosticsFailure(error, failures); }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
            { RecordDiagnosticsFailure(error, failures); }
            RequestCqrsRf3Wave.SaveFailureEvidence(diagnostics, failures);
            diagnostics = null;
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static void RecordDiagnosticsFailure(Exception error, List<Exception> failures)
    {
        var terminal = error is AggregateException aggregate ? aggregate.InnerExceptions : [error];
        foreach (var failure in terminal)
        {
            if (!failures.Contains(failure))
            { failures.Add(failure); }
        }
    }

    private async Task DisposeSubscriberObserverAsync(List<Exception> failures)
    {
        var observer = subscriberObserver;
        if (observer is null)
        { return; }
        await ServerFailureObserver.ObserveAsync(() => observer.DisposeAsync().AsTask(), failures)
            .ConfigureAwait(false);
        await JoinSubscriberObserverAsync(failures).ConfigureAwait(false);
    }

    private async Task JoinSubscriberObserverAsync(List<Exception> failures)
    {
        var observer = subscriberObserver;
        if (observer is null)
        { return; }
        if (!observer.IsJoined)
        { await observer.RetryFailedCloseAsync(failures).ConfigureAwait(false); }
        if (!observer.IsJoined)
        { failures.Add(new InvalidOperationException("The Aspire subscriber observer did not join its original stream.")); }
        if (observer.IsJoined)
        { subscriberObserver = null; }
    }

    private static string[] CreateArguments(string dataRoot, IReadOnlyDictionary<string, string> images,
        bool configureCohort, string? snapshotThresholdArgument, RequestCqrsProbeFixture? controls)
    {
        var args = new List<string>
        {
            RequestCqrsRf3Protocol.DataRootPrefix + dataRoot,
            RequestCqrsRf3Protocol.Ephemeral
        };
        if (snapshotThresholdArgument is not null)
        { args.Add(snapshotThresholdArgument); }
        if (configureCohort)
        {
            args.Add(RequestCqrsRf3Protocol.CohortEnabled);
            foreach (var node in Nodes())
            { args.Add(RequestCqrsRf3Protocol.VoterPrefix + node + "=" + images[node]); }
        }
        if (controls is not null)
        {
            args.Add("--KeyLoadTests:RequestCqrsProbe:Enabled=true");
            args.Add("--KeyLoadTests:RequestCqrsProbe:Root=" + controls.Root);
            args.Add("--KeyLoadTests:RequestCqrsProbe:SessionId=" + controls.SessionId);
        }
        return [.. args];
    }

    private static async Task WaitForNodesAsync(DistributedApplication app, bool requireHealthy,
        CancellationToken cancellationToken)
    {
        foreach (var node in Nodes())
        {
            if (requireHealthy)
            {
                await app.ResourceNotifications.WaitForResourceHealthyAsync(node, cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }
            await app.ResourceNotifications.WaitForResourceAsync(node,
                resource => resource.Snapshot.State?.Text == KnownResourceStates.Running
                    || resource.Snapshot.State?.Text == KnownResourceStates.FailedToStart,
                cancellationToken).ConfigureAwait(false);
            if (!app.ResourceNotifications.TryGetCurrentState(node, out var state)
                || state?.Snapshot.State?.Text != KnownResourceStates.Running)
            {
                throw new InvalidOperationException("An expected mixed-protocol Aspire node did not reach Running.");
            }
        }
    }

    private static Dictionary<string, string> ContainerNames(IDistributedApplicationTestingBuilder builder)
        => builder.Resources.OfType<ContainerResource>()
            .Where(resource => IsNode(resource.Name))
            .ToDictionary(resource => resource.Name,
                resource => resource.Annotations.OfType<ContainerNameAnnotation>().Single().Name,
                StringComparer.Ordinal);

    private static ContainerResource[] NodeResources(IDistributedApplicationTestingBuilder builder)
        => builder.Resources.OfType<ContainerResource>().Where(resource => IsNode(resource.Name)).ToArray();

    private static string[] Nodes()
        => [RequestCqrsRf3Protocol.Node1, RequestCqrsRf3Protocol.Node2, RequestCqrsRf3Protocol.Node3];

    private static bool IsNode(string name) => name is RequestCqrsRf3Protocol.Node1
        or RequestCqrsRf3Protocol.Node2 or RequestCqrsRf3Protocol.Node3;
}
