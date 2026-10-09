using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.ClusterRouting.Helpers;
using KeyLoad.Orleans;
using KeyLoad.Query;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed class RequestCqrsRf3WaveStartup(string dataRoot, IReadOnlyDictionary<string, string> images,
    bool configureCohort, bool requireHealthy, string? snapshotThresholdArgument, Guid diagnosticsWaveId,
    RequestCqrsProbeFixture? controls, string? physicalShardOverrideNode,
    Guid? physicalShardOverrideId, RequestCqrsLifecycleEvidence? lifecycleEvidence, QueryExecutionOptions? queryExecution,
    KeyLoad.IntegrationTests.Features.ClusterReplication.LocalRf3ImageSelection.Selection? selection,
    CancellationToken cancellationToken) : IAsyncDisposable
{
    private const string MissingWaveMessage = "The C1 Aspire wave did not transfer its owned resources.";
    private const string IncompleteNativeAdmission = "The native C1 wave did not admit all three owned instances.";
    private const string IncompletePhysicalShardOverride = "The physical shard override requires both a node and identity.";
    private readonly string[] args = RequestCqrsRf3WaveArguments.Create(dataRoot, images, configureCohort, snapshotThresholdArgument, controls, selection);
    private DistributedApplication? application;
    private IDistributedApplicationTestingBuilder? testingBuilder;
    private RequestCqrsRf3Diagnostics? diagnostics;
    private RequestCqrsRf3NativeStartupAdmission? nativeAdmission;
    private RequestCqrsRf3Wave? wave;
    private Action<RequestCqrsLifecycleStage>? FailureObserver => lifecycleEvidence is null
        ? null : lifecycleEvidence.RecordOwnerFailure;

    internal static async Task<RequestCqrsRf3Wave> StartAsync(string dataRoot,
        IReadOnlyDictionary<string, string> images, bool configureCohort, bool requireHealthy,
        string? snapshotThresholdArgument, Guid diagnosticsWaveId, CancellationToken cancellationToken,
        RequestCqrsProbeFixture? controls = null, string? physicalShardOverrideNode = null,
        Guid? physicalShardOverrideId = null, RequestCqrsLifecycleEvidence? lifecycleEvidence = null,
        QueryExecutionOptions? queryExecution = null,
        KeyLoad.IntegrationTests.Features.ClusterReplication.LocalRf3ImageSelection.Selection? selection = null)
    {
        return await RequestCqrsRf3StartupExecution.RunAsync(() => new RequestCqrsRf3WaveStartup(dataRoot, images, configureCohort,
            requireHealthy, snapshotThresholdArgument, diagnosticsWaveId, controls,
            physicalShardOverrideNode, physicalShardOverrideId, lifecycleEvidence, queryExecution, selection, cancellationToken)).ConfigureAwait(false);
    }

    private void ApplyPhysicalShardOverride(IDistributedApplicationTestingBuilder builder)
    {
        if (physicalShardOverrideNode is null && physicalShardOverrideId is null)
        { return; }
        if (physicalShardOverrideNode is null || physicalShardOverrideId is not { } identity)
        { throw new ArgumentException(IncompletePhysicalShardOverride); }
        ClusterFixturePhysicalShardIdentity.OverrideNode(builder, physicalShardOverrideNode, identity);
    }

    internal async Task<RequestCqrsRf3Wave> RunAsync()
    {
        using var deadlineTimeout = new CancellationTokenSource(RequestCqrsRf3Protocol.WaveDeadline, TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadlineTimeout.Token);
        var failures = new List<Exception>();
        lifecycleEvidence?.SetWaveToken(deadline.Token);
        lifecycleEvidence?.SetStage(RequestCqrsLifecycleStage.WaveStartup);
        await ServerFailureObserver.ObserveAsync(() => StartCoreAsync(deadline.Token), failures)
            .ConfigureAwait(false);
        if (failures.Count == 0 && wave is { } startedWave)
        {
            wave = null;
            return startedWave;
        }
        if (failures.Count == 0)
        { failures.Add(new InvalidOperationException(MissingWaveMessage)); }
        lifecycleEvidence?.RecordFirstFailure();
        ServerFailureObserver.ThrowIfAny(failures);
        throw new InvalidOperationException(MissingWaveMessage);
    }

    private async Task StartCoreAsync(CancellationToken cancellationToken)
    {
        lifecycleEvidence?.SetStage(RequestCqrsLifecycleStage.WaveStartup);
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.KeyLoad_AppHost>(args,
            cancellationToken).ConfigureAwait(false);
        testingBuilder = builder;
        ApplyPhysicalShardOverride(builder);
        ClusterFixtureQueryResultLimits.Configure(builder, queryExecution);
        builder.Services.AddLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddConsole();
            logging.SetMinimumLevel(LogLevel.Warning);
        });
        var containers = ContainerNames(builder);
        var nodeResources = NodeResources(builder);
        nativeAdmission = new(nodeResources, diagnosticsWaveId, lifecycleEvidence, FailureObserver, cancellationToken);
        builder.Eventing.Subscribe<BeforeResourceStartedEvent>(nativeAdmission.BeforeResourceStartsAsync);
        application = await builder.BuildAsync(cancellationToken).ConfigureAwait(false);
        var localImageIdentity = await RequestCqrsRf3ImageProof.VerifyModelAsync(application, images,
            cancellationToken, selection).ConfigureAwait(false);
        nativeAdmission.ReleaseVerifiedModel();
        await application.StartAsync(cancellationToken).ConfigureAwait(false);
        lifecycleEvidence?.SetStage(RequestCqrsLifecycleStage.NodeReadiness);
        await RequestCqrsRf3WaveReadiness.WaitForNodesAsync(application, requireHealthy,
            cancellationToken, lifecycleEvidence).ConfigureAwait(false);
        await RequestCqrsRf3ImageProof.VerifyStartedContainersAsync(localImageIdentity, containers, cancellationToken)
            .ConfigureAwait(false);
        var runtime = new ContainerRuntimeControl(application, containers,
            ClusterFixtureDiagnostics.FindRepositoryRoot().FullName);
        diagnostics = nativeAdmission.Diagnostics;
        if (nativeAdmission.AdmittedNodeCount != RequestCqrsRf3Protocol.NodeCount)
        { throw new InvalidOperationException(IncompleteNativeAdmission); }
        wave = RequestCqrsRf3Wave.TransferOwned(dataRoot, runtime, ref application, ref diagnostics, ref testingBuilder,
            ref nativeAdmission, lifecycleEvidence);
    }

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        if (nativeAdmission is not null)
        {
            try
            { await nativeAdmission.DisposeAsync().ConfigureAwait(false); }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { AppendFailure(failures, error, RequestCqrsLifecycleStage.ObserverJoin); }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { AppendFailure(failures, error, RequestCqrsLifecycleStage.ObserverJoin); }
            diagnostics ??= nativeAdmission.Diagnostics;
        }
        if (wave is not null)
        {
            try
            { await wave.DisposeAsync().ConfigureAwait(false); }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { AppendFailure(failures, error, RequestCqrsLifecycleStage.AuthorityWaveStop); }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { AppendFailure(failures, error, RequestCqrsLifecycleStage.AuthorityWaveStop); }
            wave = null;
        }
        if (application is not null)
        {
            await RequestCqrsLifecycleFailureObserver.ObserveAsync(
                () => RequestCqrsRf3Wave.CompleteAsync(application, diagnostics, failures, lifecycleEvidence),
                failures, FailureObserver, RequestCqrsLifecycleStage.AuthorityWaveAppStop)
                .ConfigureAwait(false);
            application = null;
        }
        var ownedDiagnostics = diagnostics;
        if (ownedDiagnostics is not null)
        {
            try
            { await ownedDiagnostics.DisposeAsync().ConfigureAwait(false); }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { RecordDiagnosticsFailure(error, failures, FailureObserver, RequestCqrsLifecycleStage.AuthorityWaveDiagnosticsDrain); }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { RecordDiagnosticsFailure(error, failures, FailureObserver, RequestCqrsLifecycleStage.AuthorityWaveDiagnosticsDrain); }
            if (failures.Count > 0)
            {
                RequestCqrsLifecycleFailureObserver.Observe(
                    () => ownedDiagnostics.SaveFailureEvidence(failures[0]), failures,
                    FailureObserver, RequestCqrsLifecycleStage.AuthorityWaveDiagnosticsArtifact);
            }
            diagnostics = null;
        }
        if (await RequestCqrsRf3BuilderCleanup.DisposeAsync(testingBuilder, failures, FailureObserver).ConfigureAwait(false))
        { testingBuilder = null; }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private void AppendFailure(List<Exception> failures, Exception failure, RequestCqrsLifecycleStage stage)
        => RequestCqrsLifecycleFailureObserver.Append(failures, failure, FailureObserver, stage);

    private static void RecordDiagnosticsFailure(Exception error, List<Exception> failures,
        Action<RequestCqrsLifecycleStage>? failureObserver, RequestCqrsLifecycleStage stage)
    {
        var terminal = error is AggregateException aggregate ? aggregate.InnerExceptions : [error];
        foreach (var failure in terminal)
        {
            if (!failures.Contains(failure))
            { RequestCqrsLifecycleFailureObserver.Append(failures, failure, failureObserver, stage); }
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

    private static bool IsNode(string name) => name is RequestCqrsRf3Protocol.Node1
        or RequestCqrsRf3Protocol.Node2 or RequestCqrsRf3Protocol.Node3;
}
