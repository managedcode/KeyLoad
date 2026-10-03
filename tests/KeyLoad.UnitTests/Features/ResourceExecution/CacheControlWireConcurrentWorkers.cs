using KeyLoad.Orleans.Features.ResourceExecution;
using static KeyLoad.UnitTests.Features.ResourceExecution.CacheControlWireConcurrencySupport;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class CacheControlWireConcurrentWorkers
{
    internal static ConcurrentResult SignDuringDisposal(CacheControlAuthenticator authenticator,
        CacheReadyProof proof, BarrierState barrier)
    {
        var signed = TrySignProof(authenticator, proof) ? 1 : 0;
        SignalReady(barrier);
        barrier.Start.Task.WaitAsync(JoinTimeout).GetAwaiter().GetResult();
        var closed = SignUntilDisposed(authenticator, proof, ref signed);
        if (closed == 0)
        {
            barrier.Disposed.Task.WaitAsync(JoinTimeout).GetAwaiter().GetResult();
            closed = ThrowsAfterDisposal(authenticator, proof) ? 1 : 0;
        }

        return new(signed, closed);
    }

    internal static void DisposeDuringWorkers(CacheControlAuthenticator authenticator, BarrierState barrier)
    {
        SignalReady(barrier);
        barrier.Start.Task.WaitAsync(JoinTimeout).GetAwaiter().GetResult();
        DisposeAndSignal(authenticator, barrier);
    }

    internal static void DisposeAndSignal(CacheControlAuthenticator authenticator, BarrierState barrier)
    {
        authenticator.Dispose();
        barrier.Disposed.TrySetResult();
    }

    private static void SignalReady(BarrierState barrier)
    {
        if (Interlocked.Increment(ref barrier.ReadyCount) == WorkerCount + 1)
        {
            barrier.Ready.TrySetResult();
        }
    }

    private static int SignUntilDisposed(CacheControlAuthenticator authenticator, CacheReadyProof proof, ref int signed)
    {
        for (var index = 0; index < AttemptsPerWorker; index++)
        {
            try
            {
                if (TrySignProof(authenticator, proof))
                {
                    signed++;
                }
            }
            catch (ObjectDisposedException)
            {
                return AttemptsPerWorker - index;
            }
        }

        return 0;
    }

    private static bool TrySignProof(CacheControlAuthenticator authenticator, CacheReadyProof proof)
        => authenticator.TrySign(proof, out var signed) && signed is not null;

    private static bool ThrowsAfterDisposal(CacheControlAuthenticator authenticator, CacheReadyProof proof)
    {
        try
        {
            _ = authenticator.TrySign(proof, out _);
            return false;
        }
        catch (ObjectDisposedException)
        {
            return true;
        }
    }

    internal sealed class BarrierState
    {
        internal int ReadyCount;
        internal TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Start { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private const int AttemptsPerWorker = 64;
}
