using KeyLoad.Orleans;
using ManagedCode.Communication;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class NativeRequestWorkProbe
{
    private const string CallbackMessage = "The native request cancellation callback failed.";
    private readonly TaskCompletionSource handlerEntered = NewSignal();
    private readonly TaskCompletionSource callbackRegistered = NewSignal();
    private readonly TaskCompletionSource callbackEntered = NewSignal();
    private readonly TaskCompletionSource producerRelease = NewSignal();
    private readonly TaskCompletionSource finallyEntered = NewSignal();
    private readonly TaskCompletionSource producerSettled = NewSignal();
    private readonly TaskCompletionSource activationSettled = NewSignal();
    private int producerSettleCount;
    private int activationSettleCount;
    private int settlementSequence;
    private int producerOrder;
    private int activationOrder;

    internal Task HandlerEntered => handlerEntered.Task;
    internal Task CallbackRegistered => callbackRegistered.Task;
    internal Task CallbackEntered => callbackEntered.Task;
    internal Task FinallyEntered => finallyEntered.Task;
    internal Task ProducerSettled => producerSettled.Task;
    internal Task ActivationSettled => activationSettled.Task;
    internal int ProducerSettleCount => Volatile.Read(ref producerSettleCount);
    internal int ActivationSettleCount => Volatile.Read(ref activationSettleCount);
    internal int ProducerOrder => Volatile.Read(ref producerOrder);
    internal int ActivationOrder => Volatile.Read(ref activationOrder);
    internal InvalidOperationException CallbackFailure { get; } = new(CallbackMessage);

    internal async ValueTask<Result<GrainOperationReply>> HoldAsync(
        ICqrsStreamWriter<GrainRequestProgress, GrainOperationReply> writer, Guid requestId, bool throwOnCancellation)
    {
        var registration = default(CancellationTokenRegistration);
        try
        {
            handlerEntered.TrySetResult();
            await writer.StartedAsync(new GrainRequestProgress(requestId));
            registration = throwOnCancellation
                ? writer.CancellationToken.Register(ThrowCallback)
                : default;
            if (throwOnCancellation)
            {
                callbackRegistered.TrySetResult();
            }
            await producerRelease.Task.WaitAsync(writer.CancellationToken).ConfigureAwait(true);

            return Result<GrainOperationReply>.Succeed(new GrainOperationReply { Payload = new byte[] { 1 } });
        }
        finally
        {
            finallyEntered.TrySetResult();
            try
            {
                await producerRelease.Task.ConfigureAwait(true);
            }
            finally
            {
                await registration.DisposeAsync().ConfigureAwait(true);
                Interlocked.Increment(ref producerSettleCount);
                Volatile.Write(ref producerOrder, Interlocked.Increment(ref settlementSequence));
                producerSettled.TrySetResult();
            }
        }
    }

    internal void ReleaseProducer() => producerRelease.TrySetResult();

    internal void SettleActivation()
    {
        Interlocked.Increment(ref activationSettleCount);
        Volatile.Write(ref activationOrder, Interlocked.Increment(ref settlementSequence));
        activationSettled.TrySetResult();
    }

    private void ThrowCallback()
    {
        callbackEntered.TrySetResult();
        throw CallbackFailure;
    }

    private static TaskCompletionSource NewSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
