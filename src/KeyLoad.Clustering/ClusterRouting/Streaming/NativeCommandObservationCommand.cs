namespace KeyLoad.Clustering;

/// <summary>Retains the original command lifetime across prepare return until its one business invocation joins.</summary>
internal sealed class NativeCommandObservationCommand
{
    private NativeCapabilityWorkLifetime? work;
    private NativeRequestWorkLease? producer;
    private readonly TaskCompletionSource<NativeRequestWorkOriginalSettlement> joined = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task<NativeRequestWorkOriginalSettlement>? settlement;
    private bool executing;
    internal Exception? SettlementFailure
    {
        get; private set;
    }
    internal NativeCapabilityWorkLifetime Work => Volatile.Read(ref work)
        ?? throw Errors.Fail(ErrorCode.OwnershipLost, NativeRequestWorkLimits.LeaseIdentityMessage);
    internal bool Owns(NativeCapabilityWorkLifetime supplied) => ReferenceEquals(Volatile.Read(ref work), supplied);

    internal void AttachAtGate(NativeCapabilityWorkLifetime actualWork)
    {
        if (work is not null || settlement is not null)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, NativeRequestWorkLimits.LeaseIdentityMessage);
        }
        producer = actualWork.OriginalProducer;
        work = actualWork;
    }

    internal void BeginAtGate()
    {
        if (executing || settlement is not null || work is null)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, NativeRequestWorkLimits.LeaseIdentityMessage);
        }
        executing = true;
    }

    internal (Task<NativeRequestWorkOriginalSettlement> Task, TaskCompletionSource? Start) SelectFinishAtGate(
        Exception? original, bool abort)
    {
        if (settlement is not null)
        {
            return (settlement, null);
        }
        if (abort && executing)
        {
            return (joined.Task, null);
        }
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        settlement = FinishAsync(start.Task, original);
        return (settlement, start);
    }

    private async Task<NativeRequestWorkOriginalSettlement> FinishAsync(Task start, Exception? original)
    {
        await start.ConfigureAwait(false);
        var actual = work;
        var originalProducer = producer;
        var failure = await NativeCommandObservationResources.ObserveAsync(
            () => NativeCapabilityWorkLifetime.SettleAsync(original, actual, actual?.FailureTelemetry(), null)).ConfigureAwait(false);
        SettlementFailure = failure;
        var outcome = originalProducer is not null
            ? await originalProducer.Settlement.ConfigureAwait(false)
            : new NativeRequestWorkOriginalSettlement(original, failure is null ? [] : [failure]);
        joined.TrySetResult(outcome);
        return outcome;
    }
}
