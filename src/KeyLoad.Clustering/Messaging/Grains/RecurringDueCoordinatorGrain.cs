using KeyLoad.Core;
using KeyLoad.Core.Features.Messaging;
using ManagedCode.Communication.CQRS;
using ManagedCode.Orleans.Graph.Extensions;
using Microsoft.Extensions.Options;
using Orleans.DurableJobs;
using Orleans.Placement;
using Orleans.Serialization;

namespace KeyLoad.Clustering;

[Immovable(ImmovableKind.Any)]
[GrainType(DueCoordinatorAliases.Coordinator), PreferLocalPlacement]
internal sealed class RecurringDueCoordinatorGrain(GrainRequestCodec codec, BackgroundStorageClient storage,
    IServiceProvider services, TimeProvider clock,
    Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> chunkSerializer,
    IOptions<DueCoordinationOptions> options, IOptions<GrainRoutingOptions> routingOptions,
    ILocalDurableJobManager durableJobs, RuntimeJournalAdmission runtimeJournalAdmission)
    : Grain, IRecurringDueCoordinatorGrain, IDurableJobHandler
{
    public async Task<DueDispatchResult> ProcessDueAsync(DueWorkHint hint, CancellationToken cancellationToken)
    {
        using var admission = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,
            runtimeJournalAdmission.SchedulingToken);
        using var timeout = new CancellationTokenSource(options.Value.DispatchDeadline, clock);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(admission.Token, timeout.Token);
        var token = deadline.Token;
        ValidateHint(hint);
        var principal = await storage.Operations.ReadDueCreatorAsync(hint, token).ConfigureAwait(true);

        if (hint.Kind == DueWorkKind.Saga)
        {
            if (principal is not null)
            {
                var schedule = SagaTimeoutJobContract.CreateScheduleRequest(this.GetGrainId(),
                    clock.GetUtcNow(), hint);
                await durableJobs.ScheduleJobAsync(schedule, token).ConfigureAwait(true);
            }

            return new(null);
        }

        var creator = principal ?? throw Errors.Fail(ErrorCode.Corruption, DueCoordinatorFields.InvalidHint);
        var commandId = DueDispatchCommandIdentity.ForDue(hint);
        var mutation = RecurringDueCommand.Create(hint);
        var payload = OrleansSerialization.Serialize(new CommandRequest(commandId, hint.Lane.Partition, [mutation]));
        var result = await DispatchWithOneUncertaintyRetry(creator, commandId, payload, token).ConfigureAwait(true);
        return new(result.Error);
    }

    public Task<DueDispatchResult> ProcessQueueDeadlineAsync(QueueDeadlineHint hint, CancellationToken cancellationToken)
        => QueueDeadlineCoordination.ExecuteAsync(hint, this.GetPrimaryKeyString(), storage,
            clock, options, runtimeJournalAdmission, DispatchWithOneUncertaintyRetry, cancellationToken);

    public Task<DueDispatchResult> ProcessQueueTransferAsync(RemoteTransferCoordinationHint hint, CancellationToken cancellationToken)
        => RemoteTransferCoordination.ExecuteAsync(hint, this.GetPrimaryKeyString(), codec,
            new(codec, storage, hint, services, GrainFactory, clock, chunkSerializer, routingOptions),
            clock, options, runtimeJournalAdmission, cancellationToken);

    public Task ExecuteJobAsync(IJobRunContext context, CancellationToken attemptCancellationToken)
        => RequestContextHelper.RunWithCurrentCallerAsync(typeof(RecurringDueCoordinatorGrain).FullName!,
            nameof(IDurableJobHandler.ExecuteJobAsync),
            () => ExecuteTimeoutAsync(context, attemptCancellationToken));

    private async Task ExecuteTimeoutAsync(IJobRunContext context, CancellationToken attemptCancellationToken)
    {
        var hint = DecodeJob(context);
        using var timeout = new CancellationTokenSource(options.Value.DispatchDeadline, clock);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(attemptCancellationToken, timeout.Token);
        var token = deadline.Token;
        var principal = await storage.Operations.ReadDueCreatorAsync(hint, token).ConfigureAwait(true);
        if (principal is null)
        {
            return;
        }

        var result = await DispatchTimeoutAsync(hint, principal, token).ConfigureAwait(true);
        await SettleTimeoutResultAsync(hint, result, token).ConfigureAwait(true);
    }

    private DueWorkHint DecodeJob(IJobRunContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var job = context.Job;
        if (job is null || job.Name != SagaTimeoutJobContract.JobName
            || job.TargetGrainId != this.GetGrainId())
        {
            throw Errors.Fail(ErrorCode.Validation, SagaTimeoutJobContract.InvalidJob);
        }

        var hint = SagaTimeoutJobContract.Parse(job.Metadata);
        ValidateHint(hint);
        return hint;
    }

    private Task<GrainOperationReply> DispatchTimeoutAsync(DueWorkHint hint,
        PrincipalRecord principal, CancellationToken cancellationToken)
    {
        var commandId = DueDispatchCommandIdentity.ForDue(hint);
        var payload = OrleansSerialization.Serialize(new CommandRequest(commandId, hint.Lane.Partition,
            [new ExpireSaga(hint.Lane, hint.Id, hint.Revision)]));
        return DispatchWithOneUncertaintyRetry(principal, commandId, payload, cancellationToken);
    }

    private async Task SettleTimeoutResultAsync(DueWorkHint hint, GrainOperationReply result,
        CancellationToken cancellationToken)
    {
        if (result.Error is null)
        {
            return;
        }

        if (result.Error is ErrorCode.PermissionDenied or ErrorCode.Unauthenticated)
        {
            return;
        }

        if (result.Error is ErrorCode.NotFound or ErrorCode.RevisionConflict or ErrorCode.UnknownWriteOutcome)
        {
            var current = await storage.Operations.ReadDueCreatorAsync(hint, cancellationToken).ConfigureAwait(true);
            if (current is null)
            {
                return;
            }
        }

        var detail = result.Error switch
        {
            ErrorCode.RevisionConflict => RecurringSagaProtocol.RevisionConflict,
            ErrorCode.UnknownWriteOutcome => SagaTimeoutJobContract.UnresolvedOutcome,
            _ => result.SafeDetail ?? SagaTimeoutJobContract.InvalidJob
        };
        throw Errors.Fail(result.Error.Value, detail);
    }

    private async Task<GrainOperationReply> DispatchWithOneUncertaintyRetry(PrincipalRecord principal,
        Guid commandId, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        var requestId = Guid.NewGuid();
        var first = await Dispatch(principal, requestId, commandId, payload, cancellationToken).ConfigureAwait(true);
        if (first.Error != ErrorCode.UnknownWriteOutcome
            || options.Value.UncertaintyRetryCount == DueCoordinationOptions.NoUncertaintyRetries)
        {
            return first;
        }
        cancellationToken.ThrowIfCancellationRequested();
        requestId = Guid.NewGuid();
        return await Dispatch(principal, requestId, commandId, payload, cancellationToken).ConfigureAwait(true);
    }

    private async Task<GrainOperationReply> Dispatch(PrincipalRecord principal, Guid requestId,
        Guid commandId, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        var connectionId = ConnectionExecutionIdentity.Resolve(services);
        using var identity = new GrainRequestIdentityScope(services, principal, requestId, commandId, cancellationToken, connectionId: connectionId);
        var envelope = codec.CreateCommand(requestId, principal.Id, OperationKind.Batch, commandId, payload);
        var request = GrainFactory.GetGrain<IConnectionGrain>(connectionId);
        return await GrainRequestStreamConsumer.DrainAsync(
            createStream: token => request.ExecuteStreamAsync(envelope, token), serializer: chunkSerializer, requestId: requestId, clock: clock,
            cancellationToken: cancellationToken, options: routingOptions).ConfigureAwait(true);
    }

    private void ValidateHint(DueWorkHint hint)
    {
        if (hint is null || hint.Lane is null || hint.Lane.Partition is null
            || hint.Lane.Partition.AtomicPartitionId != this.GetPrimaryKeyString()
            || hint.Id == Guid.Empty || string.IsNullOrWhiteSpace(hint.CreatorPrincipalId)
            || hint.Revision < DueCoordinatorFields.FirstRevision
            || hint.Generation < DueCoordinatorFields.NoGeneration || hint.Ordinal < DueCoordinatorFields.FirstOrdinal
            || !Enum.IsDefined(hint.Kind)
            || hint.Kind == DueWorkKind.Schedule && hint.Generation < DueCoordinatorFields.FirstScheduleGeneration
            || hint.Kind == DueWorkKind.Saga && hint.Generation != DueCoordinatorFields.NoGeneration
            || hint.DueAt.Offset != TimeSpan.Zero)
        {
            throw Errors.Fail(ErrorCode.Validation, DueCoordinatorFields.InvalidHint);
        }
        EngineResourceAccess.ValidatePartition(hint.Lane.Partition);
        JsonData.Identifier(hint.Lane.Queue);
        JsonData.Identifier(hint.CreatorPrincipalId);
    }
}
