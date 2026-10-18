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
    BackgroundStorageClient storage, RuntimeJournalAdmission runtime,
    GrainRequestCodec codec, IServiceProvider services, TimeProvider clock,
    Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> serializer,
    IOptions<GrainRoutingOptions> routing, IOptions<RuntimeJournalOptions> journal)
    : Grain, IAnnProjectionCoordinatorGrain, IDurableJobHandler
{
    private readonly AnnProjectionJobStateOwner owner = new(state, journal, codec.Limits.MaxBatchBytes);
    private readonly AnnProjectionJobExecution execution = new(storage,
        new(codec, services, clock, serializer, routing));

    public Task ScheduleAsync(ProjectionConsumerRef consumer, long generation, Guid nodeId, CancellationToken cancellationToken)
        => AnnProjectionJobOperation.RunAsync(services, codec.OperationLimits,
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
        var budget = new ReadExecutionBudget(codec.OperationLimits, clock, token);
        budget.ConstrainLifetime(expiry);
        budget.AttachResourceLifetimeBinding(binding);
        var target = await storage.ResolveAnnOwnerAsync(nodeId, token).ConfigureAwait(true);
        var current = owner.Current;
        if (current is null)
        {
            current = await execution.PrepareAsync(target, consumer, generation, nodeId, null, budget, expiry, binding, token).ConfigureAwait(true);
            if (current is null)
            {
                return;
            }
            await WriteAsync(current, binding, token).ConfigureAwait(true);
        }
        var next = await execution.PrepareAsync(target, consumer, generation, nodeId, current, budget, expiry, binding, token).ConfigureAwait(true);
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
                services, codec.OperationLimits,
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
        var budget = new ReadExecutionBudget(codec.OperationLimits, clock, token);
        budget.ConstrainLifetime(expiry);
        budget.AttachResourceLifetimeBinding(binding);
        var target = await storage.ResolveAnnOwnerAsync(original.PendingRequest.NodeId, token).ConfigureAwait(true);
        AnnMaintenanceResult result;
        try
        {
            result = await execution.ExecuteAsync(GrainFactory, target, original, budget, expiry, binding, token).ConfigureAwait(true);
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
