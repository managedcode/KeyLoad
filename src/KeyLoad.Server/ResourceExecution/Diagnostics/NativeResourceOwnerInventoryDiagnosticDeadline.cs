using KeyLoad.Clustering;
using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ResourceExecution;

/// <summary>Propagates one original bounded deadline through the barrier, inventory and signed output.</summary>
internal static class NativeResourceOwnerInventoryDiagnosticDeadline
{
    internal static async Task<(byte[] Payload, string Signature)> ReadAsync(IOptions<DatabaseLimits> limits,
        IOptions<GrainRoutingOptions> routing, TimeProvider clock,
        Func<ReadExecutionBudget, DateTimeOffset, CancellationToken, Task<(byte[] Payload, string Signature)>> read,
        CancellationToken caller)
    {
        caller.ThrowIfCancellationRequested();
        var duration = TimeSpan.FromSeconds(limits.Value.QueryDeadlineSeconds);
        if (routing.Value.RequestLifetime < duration)
        {
            duration = routing.Value.RequestLifetime;
        }
        var expiresAt = clock.GetUtcNow() + duration;
        var failures = new List<Exception>();
        var result = (Payload: Array.Empty<byte>(), Signature: string.Empty);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var deadline = new CancellationTokenSource(duration, clock);
            result = await ReadLinkedAsync(limits, clock, expiresAt, read, failures, caller, deadline.Token).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        return result;
    }

    private static async Task<(byte[] Payload, string Signature)> ReadLinkedAsync(IOptions<DatabaseLimits> limits,
        TimeProvider clock, DateTimeOffset expiresAt,
        Func<ReadExecutionBudget, DateTimeOffset, CancellationToken, Task<(byte[] Payload, string Signature)>> read,
        List<Exception> failures, CancellationToken caller, CancellationToken deadline)
    {
        var result = (Payload: Array.Empty<byte>(), Signature: string.Empty);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, deadline);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var work = new ReadExecutionBudget(limits, clock, linked.Token);
                try
                {
                    RequireCurrent(expiresAt, clock, caller, deadline);
                    linked.Token.ThrowIfCancellationRequested();
                    work.ConstrainLifetime(expiresAt);
                    result = await read(work, expiresAt, linked.Token).ConfigureAwait(false);
                    RequireCurrent(expiresAt, clock, caller, deadline);
                    linked.Token.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException) when (!caller.IsCancellationRequested
                    && (deadline.IsCancellationRequested || expiresAt <= clock.GetUtcNow()))
                {
                    caller.ThrowIfCancellationRequested();
                    throw Errors.Fail(ErrorCode.CursorExpired, ResourceOwnerInventoryNativeProtocol.Unavailable);
                }
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        return result;
    }

    private static void RequireCurrent(DateTimeOffset expiresAt, TimeProvider clock,
        CancellationToken caller, CancellationToken deadline)
    {
        caller.ThrowIfCancellationRequested();
        if (deadline.IsCancellationRequested || expiresAt <= clock.GetUtcNow())
        {
            throw Errors.Fail(ErrorCode.CursorExpired, ResourceOwnerInventoryNativeProtocol.Unavailable);
        }
    }
}
