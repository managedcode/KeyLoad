using KeyLoad.Core;
using ManagedCode.Communication.CQRS;
using ManagedCode.Orleans.Graph.Extensions;
using Microsoft.Extensions.Options;
using Orleans.DurableJobs;
using Orleans.Journaling;
using Orleans.Placement;
using Orleans.Serialization;

namespace KeyLoad.Clustering;

#pragma warning disable ORLEANSEXP005 // ADR-110: native journal-backed ANN durable coordinator only.
[Immovable(ImmovableKind.Any)]
[GrainType(AnnProjectionJobProtocol.GrainAlias), PreferLocalPlacement]
internal sealed class AnnProjectionCoordinatorGrain(IDurableStateManager state, ILocalDurableJobManager jobs,
    DatabaseEngine database, Func<ICommitCoordinator> borrowCoordinator, RuntimeJournalAdmission runtime,
    PhysicalShardRecord physicalOwner,
    Func<INativeAnnMaintenance> borrowNative, GrainRequestCodec codec, IServiceProvider services, TimeProvider clock,
    Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> serializer,
    IOptions<GrainRoutingOptions> routing, IOptions<RuntimeJournalOptions> journal)
    : Grain, IAnnProjectionCoordinatorGrain, IDurableJobHandler
{
    private readonly ICommitCoordinator coordinator = borrowCoordinator();
    private readonly AnnProjectionJobStateOwner owner = new(state, journal, database.Limits.MaxBatchBytes);
    private readonly AnnProjectionJobExecution execution = new(database, borrowNative(),
        new(codec, services, clock, serializer, routing));

    public Task ScheduleAsync(ProjectionConsumerRef consumer, long generation, Guid nodeId, CancellationToken cancellationToken)
        => AnnProjectionJobOperation.RunAsync(services, database,
            (binding) => ScheduleCoreAsync(consumer, generation, nodeId, binding), cancellationToken);

    private async Task ScheduleCoreAsync(ProjectionConsumerRef consumer, long generation, Guid nodeId,
        NativeCapabilityWorkLifetime binding)
    {
        RequireKey(consumer, generation, nodeId);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(binding.Token, runtime.SchedulingToken);
        var expiry = clock.GetUtcNow() + routing.Value.ExecutionLifetime;
        using var timeout = new CancellationTokenSource(routing.Value.ExecutionLifetime, clock);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(linked.Token, timeout.Token);
        var token = deadline.Token;
        var budget = new ReadExecutionBudget(database.OperationLimitsOptions, clock, token);
        budget.ConstrainLifetime(expiry);
        budget.AttachResourceLifetimeBinding(binding);
        await coordinator.ReadBarrierAsync(token).ConfigureAwait(true);
        var work = execution.Read(consumer, generation, budget, binding);
        RequireHost(work.Placement);
        if (work.NodeId != nodeId)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, AnnProjectionJobProtocol.Invalid);
        }
        var current = owner.Current;
        if (current is null)
        {
            current = await execution.InitializeAsync(work, budget, binding, token).ConfigureAwait(true);
            if (current is null)
            {
                return;
            }
            await WriteAsync(current, binding, token).ConfigureAwait(true);
        }
        var next = await execution.NextAsync(current, work, budget, binding, token).ConfigureAwait(true);
        if (next is null)
        {
            return;
        }
        if (next != current)
        {
            await WriteAsync(next, binding, token).ConfigureAwait(true);
            current = next;
        }
        if (current.JobId is not null)
        {
            return;
        }
        var request = current.PendingRequest
            ?? throw Errors.Fail(ErrorCode.Corruption, AnnProjectionJobProtocol.Invalid);
        binding.OriginalProducer.AdmitPublication();
        var scheduled = await jobs.ScheduleJobAsync(AnnProjectionJobMetadata.Create(this.GetGrainId(),
            clock.GetUtcNow(), request, journal.Value), token).ConfigureAwait(true);
        await WriteAsync(current with
        {
            JobId = scheduled.Id
        }, binding, token).ConfigureAwait(true);
    }

    public Task ExecuteJobAsync(IJobRunContext context, CancellationToken cancellationToken)
        => RequestContextHelper.RunWithCurrentCallerAsync(typeof(AnnProjectionCoordinatorGrain).FullName!,
            nameof(IDurableJobHandler.ExecuteJobAsync), () => AnnProjectionJobOperation.RunAsync(
                services, database,
                (binding) => ExecuteOriginalAsync(context, binding), cancellationToken));

    private async Task ExecuteOriginalAsync(IJobRunContext context, NativeCapabilityWorkLifetime binding)
    {
        owner.RequireOpen();
        var original = owner.Current
            ?? throw Errors.Fail(ErrorCode.HistoryUnavailable, AnnProjectionJobProtocol.Unresolved);
        AnnProjectionJobMetadata.Require(context, this.GetGrainId(), original);
        if (original.PendingRequest is null)
        {
            return;
        }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(binding.Token, runtime.SchedulingToken);
        var expiry = clock.GetUtcNow() + routing.Value.ExecutionLifetime;
        using var timeout = new CancellationTokenSource(routing.Value.ExecutionLifetime, clock);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(linked.Token, timeout.Token);
        var token = deadline.Token;
        var budget = new ReadExecutionBudget(database.OperationLimitsOptions, clock, token);
        budget.ConstrainLifetime(expiry);
        budget.AttachResourceLifetimeBinding(binding);
        await coordinator.ReadBarrierAsync(token).ConfigureAwait(true);
        AnnMaintenanceResult result;
        try
        {
            result = await execution.ExecuteAsync(GrainFactory, original, budget, expiry, binding, token).ConfigureAwait(true);
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
                }, binding, token)
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
        }, binding, token).ConfigureAwait(true);
    }

    private async Task WriteAsync(AnnProjectionJobState value, NativeCapabilityWorkLifetime binding,
        CancellationToken token)
    {
        try
        {
            binding.OriginalProducer.AdmitPublication();
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
