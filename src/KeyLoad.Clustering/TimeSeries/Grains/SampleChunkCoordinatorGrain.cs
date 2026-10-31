using KeyLoad.Core;
using KeyLoad.Core.TimeSeries;
using ManagedCode.Orleans.Graph.Extensions;
using Microsoft.Extensions.Options;
using Orleans.DurableJobs;
using Orleans.Journaling;
using Orleans.Placement;

namespace KeyLoad.Clustering;

#pragma warning disable ORLEANSEXP005 // Centrally pinned execution Orleans journaling required by ADR-124.
[Immovable(ImmovableKind.Any)]
[GrainType(SampleChunkJobProtocol.GrainAlias), PreferLocalPlacement]
internal sealed class SampleChunkCoordinatorGrain(IDurableStateManager state,
    ILocalDurableJobManager jobs, BackgroundStorageClient storage, IOptions<TimeSeriesExecutionOptions> timeSeries,
    RuntimeJournalAdmission runtime, GrainRequestCodec codec, IServiceProvider services, TimeProvider clock,
    IOptions<DueCoordinationOptions> execution)
    : Grain, ISampleChunkCoordinatorGrain, IDurableJobHandler
{
    private readonly IDurableDictionary<Guid, SampleChunkJobAdmission> admissions =
        state.GetOrAddDictionary<Guid, SampleChunkJobAdmission>(SampleChunkJobProtocol.Admissions);
    private readonly SampleChunkJobDispatch dispatch = new(storage, codec, services);
    private bool open = true;

    public Task ScheduleAsync(SampleChunkWorkHint hint, CancellationToken cancellationToken)
        => codec.PhaseObserver is null
            ? ScheduleCoreAsync(hint, null, cancellationToken)
            : ScheduleObservedAsync(hint, cancellationToken);

    private async Task ScheduleObservedAsync(SampleChunkWorkHint hint, CancellationToken cancellationToken)
    {
        var observed = new GrainRequestProbeIdentity(Guid.NewGuid(), hint.CommandId, hint.Creator, null,
            OperationKind.Batch);
        try
        {
            await ScheduleCoreAsync(hint, observed, cancellationToken).ConfigureAwait(true);
        }
        catch (Exception primary)
        {
            try
            {
                codec.ObserveProducerDisposed(observed, GrainContext);
            }
            catch (Exception cleanup) { throw new AggregateException(primary, cleanup); }
            throw;
        }
        codec.ObserveProducerDisposed(observed, GrainContext);
    }

    private async Task ObserveSchedulingAsync(GrainRequestProbeIdentity? identity, GrainRequestPhase phase,
        CancellationToken cancellationToken)
    {
        if (identity is { } actual && codec.PhaseObserver is { } observer)
        {
            await observer.ObserveAsync(actual, phase, GrainContext, cancellationToken).ConfigureAwait(true);
        }
    }

    private async Task ScheduleCoreAsync(SampleChunkWorkHint hint, GrainRequestProbeIdentity? observation,
        CancellationToken cancellationToken)
    {
        RequireOpen(hint);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, runtime.SchedulingToken);
        using var timeout = new CancellationTokenSource(execution.Value.DispatchDeadline, clock);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(linked.Token, timeout.Token);
        var token = deadline.Token;
        var current = await storage.Operations.ReadSampleChunkCurrentAsync(hint, token).ConfigureAwait(true);
        if (current is null || !SampleChunkWorkEligibility.SameOriginal(current, hint))
        {
            return;
        }
        if (admissions.TryGetValue(hint.CommandId, out _))
        {
            return;
        }
        await RetireSettledAsync(token).ConfigureAwait(true);
        if (admissions.Count >= timeSeries.Value.MaximumPendingChunkWindows)
        {
            if (observation is { } observed)
            {
                await RefuseObservedAdmissionAsync(hint, observed, token).ConfigureAwait(true);
            }
            throw Errors.Fail(ErrorCode.ResourceExhausted, SampleChunkJobProtocol.Exhausted);
        }
        var original = new SampleChunkJobAdmission(hint, null, null, null);
        admissions[hint.CommandId] = original;
        await PersistAsync(token).ConfigureAwait(true);
        await ObserveSchedulingAsync(observation, GrainRequestPhase.SampleChunkAdmissionPersisted, token)
            .ConfigureAwait(true);
        var runtimeRequest = SampleChunkJobMetadata.Create(this.GetGrainId(), clock.GetUtcNow(), hint);
        var job = await jobs.ScheduleJobAsync(runtimeRequest, token).ConfigureAwait(true);
        if (observation is not null)
        {
            SampleChunkJobObservation.Returned(services, hint.CommandId, job.Id,
            runtimeRequest.Metadata![SampleChunkJobProtocol.HintKey]);
        }
        await ObserveSchedulingAsync(observation, GrainRequestPhase.SampleChunkJobReturned, token)
            .ConfigureAwait(true);
        if (admissions.TryGetValue(hint.CommandId, out var retained) && retained == original)
        {
            admissions[hint.CommandId] = original with
            {
                JobId = job.Id
            };
            await PersistAsync(token).ConfigureAwait(true);
        }
    }

    private async Task RefuseObservedAdmissionAsync(SampleChunkWorkHint hint,
        GrainRequestProbeIdentity observed, CancellationToken token)
    {
        var original = Errors.Fail(ErrorCode.ResourceExhausted, SampleChunkJobProtocol.Exhausted);
        try
        {
            SampleChunkJobDiagnostics.AdmissionRefused(
                Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<
                    Microsoft.Extensions.Logging.ILogger<SampleChunkCoordinatorGrain>>(services),
                hint.CommandId, original.Code);
            await ObserveSchedulingAsync(observed, GrainRequestPhase.SampleChunkAdmissionRefused, token)
                .ConfigureAwait(true);
        }
        catch (Exception observation) { throw new AggregateException(original, observation); }
        throw original;
    }

    public Task ExecuteJobAsync(IJobRunContext context, CancellationToken cancellationToken)
        => RequestContextHelper.RunWithCurrentCallerAsync(typeof(SampleChunkCoordinatorGrain).FullName!,
            nameof(IDurableJobHandler.ExecuteJobAsync), () => ExecuteOriginalAsync(context, cancellationToken));

    private async Task ExecuteOriginalAsync(IJobRunContext context, CancellationToken cancellationToken)
    {
        var hint = SampleChunkJobMetadata.Read(context, this.GetGrainId());
        RequireOpen(hint);
        if (!admissions.TryGetValue(hint.CommandId, out var original))
        {
            return;
        }
        if (!SampleChunkWorkEligibility.SameOriginal(original.Hint, hint)
            || original.JobId is { } actualId && actualId != context.Job.Id)
        {
            throw Errors.Fail(ErrorCode.Validation, SampleChunkJobProtocol.Invalid);
        }
        if (codec.PhaseObserver is not null)
        {
            SampleChunkJobObservation.Executing(services, hint.CommandId, context.Job.Id,
            context.Job.Metadata![SampleChunkJobProtocol.HintKey]);
        }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, runtime.SchedulingToken);
        using var timeout = new CancellationTokenSource(execution.Value.DispatchDeadline, clock);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(linked.Token, timeout.Token);
        var token = deadline.Token;
        SampleChunkWorkHint? current;
        try
        {
            current = await storage.Operations.ReadSampleChunkCurrentAsync(hint, token).ConfigureAwait(true);
        }
        catch (KeyLoadException failure) when (failure.Code is ErrorCode.PermissionDenied or ErrorCode.Unauthenticated)
        {
            await BlockAsync(original, context.Job.Id, failure.Code, token).ConfigureAwait(true);
            return;
        }
        if (current is null || !SampleChunkWorkEligibility.SameOriginal(current, hint))
        {
            await RemoveAsync(hint.CommandId, token).ConfigureAwait(true);
            return;
        }
        var result = await dispatch.ExecuteAsync(GrainFactory, hint, token).ConfigureAwait(true);
        if (result.Error is ErrorCode.PermissionDenied or ErrorCode.Unauthenticated)
        {
            await BlockAsync(original, context.Job.Id, result.Error.Value, token).ConfigureAwait(true);
            return;
        }
        var after = await storage.Operations.ReadSampleChunkCurrentAsync(hint, token).ConfigureAwait(true);
        if (after is null || !SampleChunkWorkEligibility.SameOriginal(after, hint))
        {
            await RemoveAsync(hint.CommandId, token).ConfigureAwait(true);
            return;
        }
        throw Errors.Fail(result.Error ?? ErrorCode.RecoveryRequired, SampleChunkJobProtocol.Unresolved);
    }

    private Task RetireSettledAsync(CancellationToken token)
        => SampleChunkAdmissionTransitions.RetireSettledAsync(storage, () => admissions.ToDictionary(),
            RemoveAsync, token);

    private Task BlockAsync(SampleChunkJobAdmission original, string jobId, ErrorCode code, CancellationToken token)
    {
        admissions[original.Hint.CommandId] = SampleChunkAdmissionTransitions.Blocked(original, jobId, code);
        return PersistAsync(token);
    }

    private async Task RemoveAsync(Guid id, CancellationToken token)
    {
        admissions.Remove(id);
        await PersistAsync(token).ConfigureAwait(true);
    }

    private async Task PersistAsync(CancellationToken token)
    {
        try
        {
            await state.WriteStateAsync(token).ConfigureAwait(true);
        }
        catch (Exception) { open = false; DeactivateOnIdle(); throw; }
    }

    private void RequireOpen(SampleChunkWorkHint hint)
    {
        if (!open)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, SampleChunkJobProtocol.Unresolved);
        }
        SampleChunkJobValidation.Require(hint, this.GetPrimaryKeyString());
    }
}
#pragma warning restore ORLEANSEXP005
