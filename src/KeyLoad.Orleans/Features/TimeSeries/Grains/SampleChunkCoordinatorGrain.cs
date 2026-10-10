using KeyLoad.Core;
using KeyLoad.Core.Features.TimeSeries;
using ManagedCode.Communication.CQRS;
using ManagedCode.Orleans.Graph.Extensions;
using Microsoft.Extensions.Options;
using Orleans.DurableJobs;
using Orleans.Journaling;
using Orleans.Serialization;

namespace KeyLoad.Orleans;

#pragma warning disable ORLEANSEXP005 // Centrally pinned native Orleans journaling required by ADR-124.
[GrainType(SampleChunkJobProtocol.GrainAlias), global::Orleans.Placement.PreferLocalPlacement]
internal sealed class SampleChunkCoordinatorGrain(IDurableStateManager state,
    ILocalDurableJobManager jobs, DatabaseEngine database, ICommitCoordinator coordinator,
    RuntimeJournalAdmission runtime, GrainRequestCodec codec, IServiceProvider services, TimeProvider clock,
    Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> serializer,
    IOptions<GrainRoutingOptions> routing, IOptions<DueCoordinationOptions> execution)
    : Grain, ISampleChunkCoordinatorGrain, IDurableJobHandler
{
    private readonly IDurableDictionary<Guid, SampleChunkJobAdmission> admissions =
        state.GetOrAddDictionary<Guid, SampleChunkJobAdmission>(SampleChunkJobProtocol.Admissions);
    private readonly SampleChunkJobDispatch dispatch = new(database, codec, services, clock, serializer, routing);
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
        { await ScheduleCoreAsync(hint, observed, cancellationToken).ConfigureAwait(true); }
        catch (Exception primary)
        {
            try
            { codec.ObserveProducerDisposed(observed, GrainContext); }
            catch (Exception cleanup) { throw new AggregateException(primary, cleanup); }
            throw;
        }
        codec.ObserveProducerDisposed(observed, GrainContext);
    }

    private async Task ObserveSchedulingAsync(GrainRequestProbeIdentity? identity, GrainRequestPhase phase,
        CancellationToken cancellationToken)
    {
        if (identity is { } actual && codec.PhaseObserver is { } observer)
        { await observer.ObserveAsync(actual, phase, GrainContext, cancellationToken).ConfigureAwait(true); }
    }

    private async Task ScheduleCoreAsync(SampleChunkWorkHint hint, GrainRequestProbeIdentity? observation,
        CancellationToken cancellationToken)
    {
        RequireOpen(hint);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, runtime.SchedulingToken);
        using var timeout = new CancellationTokenSource(execution.Value.DispatchDeadline, clock);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(linked.Token, timeout.Token);
        var token = deadline.Token;
        await coordinator.ReadBarrierAsync(token).ConfigureAwait(true);
        var current = SampleChunkWorkRead.Current(database, hint, token);
        if (current is null || !SampleChunkWorkEligibility.SameOriginal(current, hint))
        { return; }
        if (admissions.TryGetValue(hint.CommandId, out _))
        { return; }
        await RetireSettledAsync(token).ConfigureAwait(true);
        if (admissions.Count >= database.TimeSeriesOptions.Value.MaximumPendingChunkWindows)
        {
            if (observation is { } observed)
            { await RefuseObservedAdmissionAsync(hint, observed, token).ConfigureAwait(true); }
            throw Errors.Fail(ErrorCode.ResourceExhausted, SampleChunkJobProtocol.Exhausted);
        }
        var original = new SampleChunkJobAdmission(hint, null, null, null);
        admissions[hint.CommandId] = original;
        await PersistAsync(token).ConfigureAwait(true);
        await ObserveSchedulingAsync(observation, GrainRequestPhase.SampleChunkAdmissionPersisted, token)
            .ConfigureAwait(true);
        var nativeRequest = SampleChunkJobMetadata.Create(this.GetGrainId(), clock.GetUtcNow(), hint,
            database.Limits.MaxBatchBytes);
        var job = await jobs.ScheduleJobAsync(nativeRequest, token).ConfigureAwait(true);
        if (observation is not null)
        {
            SampleChunkNativeJobObservation.Returned(services, hint.CommandId, job.Id,
            nativeRequest.Metadata![SampleChunkJobProtocol.HintKey], database.Limits.MaxBatchBytes);
        }
        await ObserveSchedulingAsync(observation, GrainRequestPhase.SampleChunkNativeJobReturned, token)
            .ConfigureAwait(true);
        if (admissions.TryGetValue(hint.CommandId, out var retained) && retained == original)
        {
            admissions[hint.CommandId] = original with { JobId = job.Id };
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
        var hint = SampleChunkJobMetadata.Read(context, this.GetGrainId(), database.Limits.MaxBatchBytes);
        RequireOpen(hint);
        if (!admissions.TryGetValue(hint.CommandId, out var original))
        { return; }
        if (!SampleChunkWorkEligibility.SameOriginal(original.Hint, hint)
            || original.JobId is { } actualId && actualId != context.Job.Id)
        { throw Errors.Fail(ErrorCode.Validation, SampleChunkJobProtocol.Invalid); }
        if (codec.PhaseObserver is not null)
        {
            SampleChunkNativeJobObservation.Executing(services, hint.CommandId, context.Job.Id,
            context.Job.Metadata![SampleChunkJobProtocol.HintKey], database.Limits.MaxBatchBytes);
        }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, runtime.SchedulingToken);
        using var timeout = new CancellationTokenSource(execution.Value.DispatchDeadline, clock);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(linked.Token, timeout.Token);
        var token = deadline.Token;
        await coordinator.ReadBarrierAsync(token).ConfigureAwait(true);
        SampleChunkWorkHint? current;
        try
        { current = SampleChunkWorkRead.Current(database, hint, token); }
        catch (KeyLoadException failure) when (failure.Code is ErrorCode.PermissionDenied or ErrorCode.Unauthenticated)
        {
            await BlockAsync(original, context.Job.Id, failure.Code, token).ConfigureAwait(true);
            return;
        }
        if (current is null || !SampleChunkWorkEligibility.SameOriginal(current, hint))
        { await RemoveAsync(hint.CommandId, token).ConfigureAwait(true); return; }
        var result = await dispatch.ExecuteAsync(GrainFactory, hint, token).ConfigureAwait(true);
        if (result.Error is ErrorCode.PermissionDenied or ErrorCode.Unauthenticated)
        {
            await BlockAsync(original, context.Job.Id, result.Error.Value, token).ConfigureAwait(true);
            return;
        }
        await coordinator.ReadBarrierAsync(token).ConfigureAwait(true);
        var after = SampleChunkWorkRead.Current(database, hint, token);
        if (after is null || !SampleChunkWorkEligibility.SameOriginal(after, hint))
        { await RemoveAsync(hint.CommandId, token).ConfigureAwait(true); return; }
        throw Errors.Fail(result.Error ?? ErrorCode.RecoveryRequired, SampleChunkJobProtocol.Unresolved);
    }

    private async Task RetireSettledAsync(CancellationToken token)
    {
        foreach (var entry in admissions.ToDictionary())
        {
            SampleChunkWorkHint? current;
            try
            { current = SampleChunkWorkRead.Current(database, entry.Value.Hint, token); }
            catch (KeyLoadException failure) when (failure.Code is ErrorCode.Unauthenticated or ErrorCode.PermissionDenied)
            { continue; }
            if (current is null || current.Revision != entry.Value.Hint.Revision
                || entry.Value.BlockedPolicyEpoch is { } epoch && current.CreatorPolicyEpoch != epoch)
            { await RemoveAsync(entry.Key, token).ConfigureAwait(true); }
        }
    }

    private Task BlockAsync(SampleChunkJobAdmission original, string jobId, ErrorCode code, CancellationToken token)
    {
        admissions[original.Hint.CommandId] = original with
        {
            JobId = jobId,
            BlockedPolicyEpoch = original.Hint.CreatorPolicyEpoch,
            SettledError = code
        };
        return PersistAsync(token);
    }

    private async Task RemoveAsync(Guid id, CancellationToken token)
    { admissions.Remove(id); await PersistAsync(token).ConfigureAwait(true); }

    private async Task PersistAsync(CancellationToken token)
    {
        try
        { await state.WriteStateAsync(token).ConfigureAwait(true); }
        catch (Exception) { open = false; DeactivateOnIdle(); throw; }
    }

    private void RequireOpen(SampleChunkWorkHint hint)
    {
        if (!open)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, SampleChunkJobProtocol.Unresolved); }
        SampleChunkJobValidation.Require(hint, this.GetPrimaryKeyString());
    }
}
#pragma warning restore ORLEANSEXP005
