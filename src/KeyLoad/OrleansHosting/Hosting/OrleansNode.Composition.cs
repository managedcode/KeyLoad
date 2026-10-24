using KeyLoad.Clustering;
using KeyLoad.Server.OrleansHosting;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>Borrows execution Orleans hosting and the silo-local storage owner for public operations.</summary>
internal sealed partial class OrleansNode(IServiceProvider applicationServices, IOptions<NodeOptions> nodeOptions,
    ILoggerFactory loggerFactory, ServerRuntimeOptions runtimeOptions, RuntimeJournalAdmission journalAdmission) : IAsyncDisposable
{
    private readonly Lock lifecycle = new();
    private readonly OrleansNodeRequestLifetime requestLifetime = new(runtimeOptions.GrainRouting);
    private readonly OrleansNodeRequestExecutor requests = new(runtimeOptions.GrainRouting, loggerFactory.CreateLogger<OrleansNode>());
    private readonly ConnectionOwnerIdentity connectionOwner = new();
    private readonly OrleansNodeClosure closure = new();
    private readonly OrleansNodeStartupWait startupWait = new();
    private Task? startup;
    private Task? shutdown;
    private bool localReady;
    private IGrainFactory? grains;
    internal RequestWorkOwner RequestWork => requestLifetime.Work;
    internal ConnectionOwnerIdentity ConnectionOwner => connectionOwner;
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

    internal void PublishRuntime(CancellationToken cancellationToken)
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
        await RuntimeJournalStartup.InitializeAsync(applicationServices,
            token => AdministratorAuthenticationRequests.StartupAsync(this, settings.AdminKey,
                runtimeOptions.McpExecution, token), runtimeOptions, cancellationToken);
        var factory = Grains ?? throw Errors.Fail(ErrorCode.OwnershipLost, OrleansNodeProtocol.RoutingUnavailable);
        await factory.GetGrain<IRecurringDueWorkerGrain>(BackgroundWorkerIdentity.Messaging()).WakeAsync(cancellationToken);
        await factory.GetGrain<ISampleChunkWorkerGrain>(BackgroundWorkerIdentity.TimeSeries()).WakeAsync(cancellationToken);
        await factory.GetGrain<IAnnProjectionWorkerGrain>(BackgroundWorkerIdentity.AnnProjection()).WakeAsync(cancellationToken);
    }

    public Task<GrainOperationReply> ExecuteAsync(Guid requestId, GrainRequestEnvelope envelope, bool command,
        CancellationToken cancellationToken)
        => requests.ExecuteAsync(CurrentRequestTarget, true, requestId, envelope, command, cancellationToken);

    internal Task<GrainOperationReply> ExecuteStartupRequestAsync(Guid requestId, GrainRequestEnvelope envelope,
        bool command, CancellationToken cancellationToken)
        => requests.ExecuteAsync(CurrentRequestTarget, false, requestId, envelope, command, cancellationToken);

    private NodeRequestTarget CurrentRequestTarget => new(Grains, applicationServices, DatabaseReady);

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
        await StopAsync(CancellationToken.None);
        if (closure.IsJoined)
        {
            await requestLifetime.DisposeAsync();
        }
    }
}
