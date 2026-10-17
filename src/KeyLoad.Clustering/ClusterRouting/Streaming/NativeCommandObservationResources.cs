using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

/// <summary>Resources of one original observation lease; none is created after the effect.</summary>
internal sealed class NativeCommandObservationResources(IOptions<DatabaseLimits> options, int maximumResultBytes) : IAsyncDisposable
{
    internal ResourceLifetimeWorkResultRetention ResultRetention { get; } = new(options);
    internal int MaximumResultBytes => maximumResultBytes;
    private readonly CancellationTokenSource stopping = new();
    private CancellationTokenSource? linked;
    private CancellationTokenSource? deadline;
    private CancellationTokenRegistration cancellationRegistration;
    private CancellationTokenRegistration deadlineRegistration;
    private ReadExecutionBudget? budget;
    internal ReadExecutionBudget Budget => budget
        ?? throw Errors.Fail(ErrorCode.OwnershipLost, NativeRequestWorkLimits.LeaseIdentityMessage);
    internal CancellationToken Token => linked?.Token ?? stopping.Token;

    internal void Initialize(NativeCommandObservation state, NativeRequestWorkLease lease,
        NativeCommandObservationAdmissionV1 admission, DateTimeOffset originalExpiry, TimeProvider clock,
        CancellationToken shutdownToken, CancellationToken callerToken)
    {
        linked = CancellationTokenSource.CreateLinkedTokenSource(callerToken, shutdownToken, lease.Cancellation, stopping.Token);
        budget = new(options, clock, linked.Token);
        Budget.ConstrainLifetime(originalExpiry);
        Budget.ConstrainNativeWork(admission.MaximumReadBytes, admission.MaximumExaminedRecords);
        Budget.ConstrainResultBytes(admission.MaximumResultBytes);
        Budget.AttachResourceLifetimeBinding(lease);
        deadline = new(Budget.RemainingLifetime, clock);
        cancellationRegistration = linked.Token.Register(static value => ((NativeCommandObservation)value!).StartAbort(), state);
        deadlineRegistration = deadline.Token.Register(static value => ((NativeCommandObservation)value!).StartAbort(), state);
    }

    internal Task<Exception?> CancelAsync() => NativeRequestWorkSettlement.CancelAsync(stopping);

    internal async Task<Exception?> DisposeAsync()
    {
        var failures = new List<Exception>();
        Add(failures, await ObserveAsync(() => cancellationRegistration.DisposeAsync().AsTask()).ConfigureAwait(false));
        Add(failures, await ObserveAsync(() => deadlineRegistration.DisposeAsync().AsTask()).ConfigureAwait(false));
        Add(failures, Dispose(deadline));
        Add(failures, Dispose(linked));
        Add(failures, Dispose(stopping));
        Add(failures, Dispose(ResultRetention));
        return failures.Count switch
        {
            NoFailures => null,
            SingleFailure => failures[NoFailures],
            _ => new AggregateException(failures)
        };
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        var failure = await DisposeAsync().ConfigureAwait(false);
        NativeRequestWorkSettlement.Rethrow(failure);
    }

    private const int NoFailures = 0;
    private const int SingleFailure = 1;

    private static void Add(List<Exception> failures, Exception? error)
    {
        if (error is not null)
        {
            failures.Add(error);
        }
    }

    internal static async Task<Exception?> ObserveAsync(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
            return null;
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            return error;
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            return error;
        }
    }

    private static Exception? Dispose(IDisposable? resource)
    {
        try
        {
            resource?.Dispose();
            return null;
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            return error;
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            return error;
        }
    }
}
