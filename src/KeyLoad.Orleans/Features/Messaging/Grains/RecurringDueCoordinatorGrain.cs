using KeyLoad.Core;
using KeyLoad.Core.Features.Messaging;
using ManagedCode.Communication.CQRS;
using ManagedCode.Orleans.Graph.Extensions;
using Microsoft.Extensions.Options;
using Orleans.DurableJobs;
using Orleans.Serialization;

namespace KeyLoad.Orleans;

[global::Orleans.GrainType(DueCoordinatorAliases.Coordinator), global::Orleans.Placement.PreferLocalPlacement]
internal sealed class RecurringDueCoordinatorGrain(GrainRequestCodec codec, DatabaseEngine database,
    ICommitCoordinator coordinator, IServiceProvider services, TimeProvider clock,
    Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> chunkSerializer,
    IOptions<DueCoordinationOptions> options, IOptions<GrainRoutingOptions> routingOptions,
    ILocalDurableJobManager durableJobs, RuntimeJournalAdmission runtimeJournalAdmission)
    : Grain, IRecurringDueCoordinatorGrain, IDurableJobHandler
{
    private readonly DueCoordinationOptions settings = options.Value;

    public async Task<DueDispatchResult> ProcessDueAsync(DueWorkHint hint, CancellationToken cancellationToken)
    {
        using var admission = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,
            runtimeJournalAdmission.SchedulingToken);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(admission.Token);
        deadline.CancelAfter(settings.DispatchDeadline);
        var token = deadline.Token;
        ValidateHint(hint);
        await coordinator.ReadBarrierAsync(token).ConfigureAwait(true);

        if (hint.Kind == DueWorkKind.Saga)
        {
            var eligibility = NativeSagaTimeoutEligibility.Read(database, hint, clock.GetUtcNow());
            if (eligibility.IsEligible)
            {
                await durableJobs.ScheduleJobAsync(new ScheduleJobRequest
                {
                    Target = this.GetGrainId(),
                    JobName = NativeSagaTimeoutJobContract.JobName,
                    DueTime = clock.GetUtcNow(),
                    Metadata = NativeSagaTimeoutJobContract.Create(hint),
                    TraceParent = string.Empty,
                    TraceState = string.Empty
                }, token).ConfigureAwait(true);
            }

            return new(null);
        }

        var principal = database.Store.Read(view => database.Principal(view, hint.CreatorPrincipalId, clock.GetUtcNow()));
        var commandId = Guid.NewGuid();
        var mutation = RecurringDueCommand.Create(hint);
        var payload = NativeSerialization.Serialize(new CommandRequest(commandId, hint.Lane.Partition, [mutation]));
        var result = await DispatchWithOneUncertaintyRetry(principal, commandId, payload, token).ConfigureAwait(true);
        return new(result.Error);
    }

    public Task ExecuteJobAsync(IJobRunContext context, CancellationToken attemptCancellationToken)
        => RequestContextHelper.RunWithCurrentCallerAsync(typeof(RecurringDueCoordinatorGrain).FullName!,
            nameof(IDurableJobHandler.ExecuteJobAsync),
            () => ExecuteNativeTimeoutAsync(context, attemptCancellationToken));

    private async Task ExecuteNativeTimeoutAsync(IJobRunContext context, CancellationToken attemptCancellationToken)
    {
        var hint = DecodeJob(context);
        if (context.Job.DueTime > clock.GetUtcNow())
        {
            return;
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(attemptCancellationToken);
        deadline.CancelAfter(settings.DispatchDeadline);
        var token = deadline.Token;
        await coordinator.ReadBarrierAsync(token).ConfigureAwait(true);
        var state = NativeSagaTimeoutEligibility.Read(database, hint, clock.GetUtcNow());
        if (!state.IsEligible)
        {
            return;
        }

        var result = await DispatchNativeTimeoutAsync(hint, state, token).ConfigureAwait(true);
        await SettleNativeTimeoutResultAsync(hint, result, token).ConfigureAwait(true);
    }

    private DueWorkHint DecodeJob(IJobRunContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var job = context.Job;
        if (job is null || job.Name != NativeSagaTimeoutJobContract.JobName
            || job.TargetGrainId != this.GetGrainId())
        {
            throw Errors.Fail(ErrorCode.Validation, NativeSagaTimeoutJobContract.InvalidJob);
        }

        var hint = NativeSagaTimeoutJobContract.Parse(job.Metadata);
        ValidateHint(hint);
        return hint;
    }

    private Task<GrainOperationReply> DispatchNativeTimeoutAsync(DueWorkHint hint,
        NativeSagaTimeoutState state, CancellationToken cancellationToken)
    {
        var commandId = Guid.NewGuid();
        var payload = NativeSerialization.Serialize(new CommandRequest(commandId, hint.Lane.Partition,
            [new ExpireSaga(hint.Lane, hint.Id, hint.Revision)]));
        return DispatchWithOneUncertaintyRetry(state.Creator!, commandId, payload, cancellationToken);
    }

    private async Task SettleNativeTimeoutResultAsync(DueWorkHint hint, GrainOperationReply result,
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

        if (result.Error is ErrorCode.RevisionConflict or ErrorCode.UnknownWriteOutcome)
        {
            await coordinator.ReadBarrierAsync(cancellationToken).ConfigureAwait(true);
            var current = NativeSagaTimeoutEligibility.Read(database, hint, clock.GetUtcNow());
            if (!current.IsEligible)
            {
                return;
            }
        }

        throw Errors.Fail(result.Error.Value, NativeSagaTimeoutJobContract.InvalidJob);
    }

    private async Task<GrainOperationReply> DispatchWithOneUncertaintyRetry(PrincipalRecord principal,
        Guid commandId, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        var requestId = Guid.NewGuid();
        var first = await Dispatch(principal, requestId, commandId, payload, cancellationToken).ConfigureAwait(true);
        if (first.Error != ErrorCode.UnknownWriteOutcome
            || settings.UncertaintyRetryCount == DueCoordinationOptions.NoUncertaintyRetries)
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
        using var identity = new GrainRequestIdentityScope(services, principal, requestId, commandId, cancellationToken);
        var signed = codec.CreateCommand(requestId, principal.Id, OperationKind.Batch, commandId, payload);
        var request = GrainFactory.GetGrain<IRequestGrain>(requestId);
        return await GrainRequestStreamConsumer.DrainAsync(
            token => request.ExecuteStreamAsync(signed, token), chunkSerializer, requestId, clock,
            cancellationToken, routingOptions).ConfigureAwait(true);
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
        DatabaseEngine.ValidatePartition(hint.Lane.Partition);
        JsonData.Identifier(hint.Lane.Queue);
        JsonData.Identifier(hint.CreatorPrincipalId);
    }
}
