using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class PlacementIntentAdmissions : IAsyncDisposable
{
    private const int SinglePermit = 1;
    private const int NoCalls = 0;
    private const string Bound = "The Control-owner placement apply admission is exhausted.";
    private readonly Lock gate = new();
    private readonly Lock shutdownGate = new();
    private Task? shutdown;
    private readonly ConcurrencyLimiter limiter;
    private readonly TaskCompletionSource joined = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int calls;
    private bool closed;

    internal PlacementIntentAdmissions(IOptions<TenantPlacementExecutionOptions> options)
    {
        options.Value.Validate();
        limiter = new(new ConcurrencyLimiterOptions
        {
            PermitLimit = options.Value.MaximumActive,
            QueueLimit = options.Value.MaximumPending,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
        });
    }

    internal async Task<PlacementIntentAdmissionLease> EnterAsync(CancellationToken token)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            token.ThrowIfCancellationRequested();
            calls = checked(calls + SinglePermit);
        }
        RateLimitLease? native = null;
        PlacementIntentAdmissionLease? result = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            native = await limiter.AcquireAsync(SinglePermit, token).ConfigureAwait(false);
            if (!native.IsAcquired)
            {
                throw Errors.Fail(ErrorCode.ResourceExhausted, Bound);
            }
            token.ThrowIfCancellationRequested();
            result = new PlacementIntentAdmissionLease(this, native);
        }, failures).ConfigureAwait(false);
        if (result is null)
        {
            ServerFailureObserver.Observe(() => native?.Dispose(), failures);
            ServerFailureObserver.Observe(Release, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw Errors.Fail(ErrorCode.Corruption, Bound);
    }

    internal void Release()
    {
        lock (gate)
        {
            if (calls <= NoCalls)
            {
                throw Errors.Fail(ErrorCode.Corruption, Bound);
            }
            calls--;
            if (closed && calls == NoCalls)
            {
                joined.TrySetResult();
            }
        }
    }

    internal void CloseAdmission()
    {
        var dispose = false;
        lock (gate)
        {
            if (!closed)
            {
                closed = true;
                dispose = true;
                if (calls == NoCalls)
                {
                    joined.TrySetResult();
                }
            }
        }
        if (dispose)
        {
            limiter.Dispose();
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (shutdownGate)
        {
            shutdown ??= StopAsync();
            return new(shutdown);
        }
    }

    private async Task StopAsync()
    {
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(CloseAdmission, failures);
        await ServerFailureObserver.ObserveAsync(() => joined.Task, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
