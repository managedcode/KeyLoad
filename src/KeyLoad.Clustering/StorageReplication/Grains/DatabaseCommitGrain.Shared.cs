using ManagedCode.Orleans.Graph.Extensions;

namespace KeyLoad.Clustering;

public sealed partial class DatabaseCommitGrain
{
    private const string SharedUnavailable = "The shared physical database owner is unavailable.";
    private ISharedDatabaseSession? shared;
    private ISharedDatabaseSession Shared => UsesSharedVolume && shared is not null
        ? shared : throw Errors.Fail(ErrorCode.OwnershipLost, SharedUnavailable);

    /// <inheritdoc />
    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        options.Value.Validate();
        if (!UsesSharedVolume)
        {
            await base.OnActivateAsync(cancellationToken).ConfigureAwait(true);
            return;
        }
        var runtime = localRuntime ?? throw Errors.Fail(ErrorCode.OwnershipLost, SharedUnavailable);
        var original = await runtime.OpenSharedAsync(((IGrainBase)this).GrainContext, cancellationToken).ConfigureAwait(true);
        shared = original;
        try
        {
            if (original.ReadCut(cancellationToken).Incarnation != this.GetPrimaryKey())
            {
                throw Errors.Fail(ErrorCode.RecoveryRequired, SharedUnavailable);
            }
            await base.OnActivateAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (Exception failure)
        {
            try
            {
                await runtime.CloseSharedAsync(original).ConfigureAwait(true);
                shared = null;
            }
            catch (Exception cleanup)
            {
                throw new AggregateException(failure, cleanup);
            }
            throw;
        }
    }

    /// <inheritdoc />
    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        if (shared is { } original)
        {
            await (localRuntime ?? throw Errors.Fail(ErrorCode.OwnershipLost, SharedUnavailable))
                .CloseSharedAsync(original).ConfigureAwait(true);
            shared = null;
        }
        await base.OnDeactivateAsync(reason, cancellationToken).ConfigureAwait(true);
    }

    /// <inheritdoc />
    public Task<GrainOperationReply> ExecuteSharedAsync(GrainRequestEnvelope envelope, string? actorKey, CancellationToken cancellationToken)
        => this.WithCallChainReentrancyAsync(() => Shared.ExecuteAsync(envelope, actorKey,
            ((IGrainBase)this).GrainContext, cancellationToken));

    /// <inheritdoc />
    public Task AbortSharedMaintenanceAsync(GrainReadKind kind, Guid sessionId)
        => Shared.AbortMaintenanceAsync(kind, sessionId);

    /// <inheritdoc />
    public Task<NodeStatus> ReadSharedStatusAsync(CancellationToken cancellationToken)
        => Shared.ReadStatusAsync(cancellationToken);

    /// <inheritdoc />
    public Task<NodeAdmissionStatus> ReadSharedAdmissionAsync(CancellationToken cancellationToken)
        => Shared.ReadAdmissionAsync(cancellationToken);

    /// <inheritdoc />
    public Task<AdminNodeSnapshot> ReadSharedDashboardAsync(CancellationToken cancellationToken)
        => Shared.ReadDashboardAsync(cancellationToken);

    /// <inheritdoc />
    public Task<BackupReceipt> CreateSharedBackupAsync(CancellationToken cancellationToken)
        => Shared.CreateBackupAsync(cancellationToken);

    private Task<T> ExecuteBackgroundAsync<T>(SiloAddress? target,
        Func<IBackgroundStorageOperations, Task<T>> execute, CancellationToken cancellationToken)
        => this.WithCallChainReentrancyAsync(() => ExecuteBackgroundCoreAsync(target, execute, cancellationToken));

    private async Task<T> ExecuteBackgroundCoreAsync<T>(SiloAddress? target,
        Func<IBackgroundStorageOperations, Task<T>> execute, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IBackgroundStorageOperations backend;
        if (UsesSharedVolume)
        {
            backend = Shared.Background;
        }
        else
        {
            var owner = target ?? await Copies.GetReadOwnerAsync(storage.LocalSiloAddress, cancellationToken).ConfigureAwait(true);
            if (!membership.CurrentSnapshot.Members.TryGetValue(owner, out var member) || member.Status != SiloStatus.Active)
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, SharedUnavailable);
            }
            backend = storage.On(owner);
        }
        return await execute(backend).ConfigureAwait(true);
    }
}
