using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>Borrows native Orleans hosting and the silo-local storage owner for public operations.</summary>
internal sealed partial class OrleansNode(IServiceProvider applicationServices, IOptions<NodeOptions> nodeOptions,
    ILoggerFactory loggerFactory, ServerRuntimeOptions runtimeOptions, TimeProvider? clock = null) : IAsyncDisposable
{
    private readonly Lock lifecycle = new();
    private readonly OrleansNodeRequestLifetime requestLifetime = new(runtimeOptions.GrainRouting);
    private readonly OrleansNodeRequestExecutor requests = new(runtimeOptions.GrainRouting, loggerFactory.CreateLogger<OrleansNode>());
    private readonly NativeConnectionOwnerIdentity connectionOwner = new();
    private readonly OrleansNodeClosure closure = new();
    private readonly OrleansNodeStartupWait startupWait = new();
    private Task? startup;
    private Task? shutdown;
    private bool localReady;
    private IGrainFactory? grains;
    private PartitionHost Partition => applicationServices.GetRequiredService<PartitionHostApplicationOwner>().Borrow();
    internal NativeRequestWorkOwner RequestWork => requestLifetime.Work;
    internal NativeConnectionOwnerIdentity ConnectionOwner => connectionOwner;
    internal Task? Closing => Volatile.Read(ref shutdown);
    public IGrainFactory? Grains => Volatile.Read(ref grains);
    public Guid InternalConnectionId => connectionOwner.Id;
    internal bool HasJoinedRequestWork => closure.IsJoined;
    internal bool SiloJoined => Grains is not null;
    internal bool DatabaseReady => SiloJoined && startup?.IsCompletedSuccessfully == true;
    internal IServiceProvider RuntimeServices => Grains is not null ? applicationServices
        : throw Errors.Fail(ErrorCode.OwnershipLost, OrleansNodeProtocol.RoutingUnavailable);

    internal Task InitializeLocalRuntimeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (lifecycle)
        {
            if (!localReady)
            {
                RequestWork.BindPhysicalOwner(applicationServices.GetRequiredService<PhysicalShardRecord>());
                localReady = true;
            }
        }
        return Task.CompletedTask;
    }

    internal void RequireLocalRuntimePublished()
    {
        if (!localReady)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, OrleansNodeProtocol.RoutingUnavailable);
        }
    }

    internal void PublishNativeRuntime(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireLocalRuntimePublished();
        Volatile.Write(ref grains, applicationServices.GetRequiredService<IGrainFactory>());
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (lifecycle)
        {
            if (startup is not null || shutdown is not null || Grains is null)
            {
                throw new InvalidOperationException(OrleansNodeProtocol.SiloAlreadyStarted);
            }
            startup = BootstrapAsync(cancellationToken);
            startupWait.Publish(startup);
            return startup;
        }
    }

    private async Task BootstrapAsync(CancellationToken cancellationToken)
    {
        var settings = nodeOptions.Value;
        await RuntimeJournalStartup.InitializeAsync(applicationServices, Partition,
            token => NativeAdministratorAuthenticationRequests.StartupAsync(this, settings.AdminKey,
                runtimeOptions.McpExecution, token), settings, runtimeOptions, cancellationToken).ConfigureAwait(false);
        var factory = Grains ?? throw Errors.Fail(ErrorCode.OwnershipLost, OrleansNodeProtocol.RoutingUnavailable);
        await factory.GetGrain<IRecurringDueWorkerGrain>(BackgroundWorkerIdentity.Messaging()).WakeAsync(cancellationToken).ConfigureAwait(false);
        await factory.GetGrain<ISampleChunkWorkerGrain>(BackgroundWorkerIdentity.TimeSeries()).WakeAsync(cancellationToken).ConfigureAwait(false);
        await factory.GetGrain<IAnnProjectionWorkerGrain>(BackgroundWorkerIdentity.AnnProjection()).WakeAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<GrainOperationReply> ExecuteAsync(Guid requestId, string signedRequest, bool command,
        CancellationToken cancellationToken)
        => requests.ExecuteAsync(CurrentRequestTarget, true, requestId, signedRequest, command, cancellationToken);

    internal Task<GrainOperationReply> ExecuteStartupRequestAsync(Guid requestId, string signedRequest,
        bool command, CancellationToken cancellationToken)
        => requests.ExecuteAsync(CurrentRequestTarget, false, requestId, signedRequest, command, cancellationToken);

    private NativeNodeRequestTarget CurrentRequestTarget => new(Grains, applicationServices, DatabaseReady);

    internal Task RequireDatabaseReadCutAsync(CancellationToken token) => Partition.Coordinator.ReadBarrierAsync(token);

    internal async Task RequireDatabaseReadCutAsync(ReadExecutionBudget work,
        KeyLoad.Core.Features.ResourceExecution.Execution.ReadExecutionBudgetReadGrant? sourceMeta, CancellationToken token)
    {
        work.Check();
        await Partition.Coordinator.ReadBarrierAsync(token).ConfigureAwait(false);
        work.Check();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        lock (lifecycle)
        {
            startupWait.Close();
            shutdown ??= StopCoreAsync();
            return shutdown.WaitAsync(cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        if (closure.IsJoined)
        {
            await requestLifetime.DisposeAsync().ConfigureAwait(false);
        }
    }
}
