using KeyLoad.Core;
using ManagedCode.Communication.CQRS;
using ManagedCode.Orleans.Graph.Extensions;
using Microsoft.Extensions.Options;
using Orleans.DurableJobs;
using Orleans.Journaling;
using Orleans.Serialization;

namespace KeyLoad.Clustering;

#pragma warning disable ORLEANSEXP005 // ADR-110: native journal-backed ANN durable coordinator only.
[Orleans.Placement.Immovable(Orleans.Placement.ImmovableKind.Any)]
[global::Orleans.GrainType(AnnProjectionJobProtocol.GrainAlias), global::Orleans.Placement.PreferLocalPlacement]
internal sealed class AnnProjectionCoordinatorGrain(IDurableStateManager state, ILocalDurableJobManager jobs,
    DatabaseEngine database, Func<ICommitCoordinator> borrowCoordinator, RuntimeJournalAdmission runtime,
    PhysicalShardRecord physicalOwner,
    Func<INativeAnnMaintenance> borrowNative, GrainRequestCodec codec, IServiceProvider services, TimeProvider clock,
    Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> serializer,
    IOptions<GrainRoutingOptions> routing, IOptions<RuntimeJournalOptions> journal,
    IOptions<DatabaseLimits> limits)
    : global::Orleans.Grain, IAnnProjectionCoordinatorGrain, IDurableJobHandler
{
    private readonly ICommitCoordinator coordinator = borrowCoordinator();
    private readonly AnnProjectionJobStateOwner owner = new(state, journal, database.Limits.MaxBatchBytes);
    private readonly AnnProjectionJobExecution execution = new(database, borrowNative(), limits, clock,
        new(codec, services, clock, serializer, routing));

    public async Task ScheduleAsync(ProjectionConsumerRef consumer, long generation, Guid nodeId, CancellationToken cancellationToken)
    {
        RequireKey(consumer, generation, nodeId);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, runtime.SchedulingToken);
        using var timeout = new CancellationTokenSource(routing.Value.ExecutionLifetime, clock);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(linked.Token, timeout.Token);
        var token = deadline.Token;
        await coordinator.ReadBarrierAsync(token).ConfigureAwait(true);
        var work = execution.Read(consumer, generation, token);
        RequireHost(work.Placement);
        if (work.NodeId != nodeId)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, AnnProjectionJobProtocol.Invalid);
        }
        var current = owner.Current;
        if (current is null)
        {
            current = await execution.InitializeAsync(work, token).ConfigureAwait(true);
            if (current is null)
            {
                return;
            }
            await WriteAsync(current, token).ConfigureAwait(true);
        }
        var next = await execution.NextAsync(current, work, token).ConfigureAwait(true);
        if (next is null)
        {
            return;
        }
        if (next != current)
        {
            await WriteAsync(next, token).ConfigureAwait(true);
            current = next;
        }
        if (current.JobId is not null)
        {
            return;
        }
        var request = current.PendingRequest
            ?? throw Errors.Fail(ErrorCode.Corruption, AnnProjectionJobProtocol.Invalid);
        var scheduled = await jobs.ScheduleJobAsync(AnnProjectionJobMetadata.Create(this.GetGrainId(),
            clock.GetUtcNow(), request, journal.Value), token).ConfigureAwait(true);
        await WriteAsync(current with
        {
            JobId = scheduled.Id
        }, token).ConfigureAwait(true);
    }

    public Task ExecuteJobAsync(IJobRunContext context, CancellationToken cancellationToken)
        => RequestContextHelper.RunWithCurrentCallerAsync(typeof(AnnProjectionCoordinatorGrain).FullName!,
            nameof(IDurableJobHandler.ExecuteJobAsync), () => ExecuteOriginalAsync(context, cancellationToken));

    private async Task ExecuteOriginalAsync(IJobRunContext context, CancellationToken cancellationToken)
    {
        owner.RequireOpen();
        var original = owner.Current
            ?? throw Errors.Fail(ErrorCode.HistoryUnavailable, AnnProjectionJobProtocol.Unresolved);
        AnnProjectionJobMetadata.Require(context, this.GetGrainId(), original);
        if (original.PendingRequest is null)
        {
            return;
        }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, runtime.SchedulingToken);
        using var timeout = new CancellationTokenSource(routing.Value.ExecutionLifetime, clock);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(linked.Token, timeout.Token);
        var token = deadline.Token;
        await coordinator.ReadBarrierAsync(token).ConfigureAwait(true);
        AnnMaintenanceResult result;
        try
        {
            result = await execution.ExecuteAsync(GrainFactory, original, token).ConfigureAwait(true);
        }
        catch (KeyLoadException failure) when (failure.Code is ErrorCode.PermissionDenied or ErrorCode.Unauthenticated
            or ErrorCode.TokenInvalidated)
        {
            try
            {
                await WriteAsync(original with
                {
                    JobId = context.Job.Id,
                    BlockedError = failure.Code
                }, token)
                    .ConfigureAwait(true);
            }
            catch (Exception cleanup) { throw new AggregateException(failure, cleanup); }
            throw;
        }
        await WriteAsync(original with
        {
            JobId = context.Job.Id,
            LastRequest = original.PendingRequest,
            PendingRequest = null,
            LastResult = result
        }, token).ConfigureAwait(true);
    }

    private async Task WriteAsync(AnnProjectionJobState value, CancellationToken token)
    {
        try
        {
            await owner.WriteAsync(value, token).ConfigureAwait(true);
        }
        catch (Exception) { DeactivateOnIdle(); throw; }
    }

    private void RequireHost(PhysicalShardRecord placement)
    {
        if (physicalOwner.PhysicalShardId != placement.PhysicalShardId
            || physicalOwner.Incarnation != placement.Incarnation
            || !physicalOwner.VoterIds.SequenceEqual(placement.VoterIds, StringComparer.Ordinal))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, AnnProjectionJobProtocol.Invalid);
        }
    }

    private void RequireKey(ProjectionConsumerRef consumer, long generation, Guid nodeId)
    {
        owner.RequireOpen();
        if (AnnProjectionJobProtocol.GrainKey(consumer, generation, nodeId) != this.GetPrimaryKeyString())
        {
            throw Errors.Fail(ErrorCode.Validation, AnnProjectionJobProtocol.Invalid);
        }
    }
}
#pragma warning restore ORLEANSEXP005
