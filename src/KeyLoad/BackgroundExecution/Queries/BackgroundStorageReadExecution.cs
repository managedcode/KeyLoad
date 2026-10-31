using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.ResourceExecution;
using ManagedCode.Communication;

namespace KeyLoad.Server;

/// <summary>Settles actual physical read work before returning its observed lifetimes to the original caller.</summary>
internal static class BackgroundStorageReadExecution
{
    internal static async Task<T> RunLocalAsync<T>(PartitionStorageScope storage,
        Func<CancellationToken, T> read, CancellationToken token)
    {
        await using var lifetime = (CapabilityWorkLifetime.Acquire(storage.Work, Guid.NewGuid(),
            RequestWorkKind.RuntimeTransport, token)).Value
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        try
        {
            lifetime.Token.ThrowIfCancellationRequested();
            return read(lifetime.Token);
        }
        catch (Exception failure) when (CqrsBoundaryErrors.IsNonFatal(failure))
        {
            lifetime.PrimaryError = failure;
            throw;
        }
        catch (Exception failure) when (!CqrsBoundaryErrors.IsNonFatal(failure))
        {
            lifetime.PrimaryError = failure;
            throw;
        }
    }

    internal static async Task<BackgroundReadResult> RunAsync<T>(PartitionStorageScope storage,
        TimeProvider clock, BackgroundReadBudget bounds,
        Func<ReadExecutionBudget, IResourceLifetimeWorkBinding, CancellationToken, Task<T>> execute,
        CancellationToken token)
    {
        Require(bounds, clock);
        await using var lifetime = (CapabilityWorkLifetime.Acquire(storage.Work, Guid.NewGuid(),
            RequestWorkKind.RuntimeTransport, token)).Value
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        var work = new ReadExecutionBudget(clock, lifetime.Token);
        work.ConstrainLifetime(bounds.ExpiresAt);
        var binding = new BackgroundStorageReadBinding(lifetime);
        work.AttachResourceLifetimeBinding(binding);
        try
        {
            Result<object> result;
            try
            {
                var value = await execute(work, binding, lifetime.Token).ConfigureAwait(true);
                result = Result<object>.Succeed(new GrainValue(value));
                work.Check();
            }
            catch (KeyLoadException failure)
            {
                lifetime.PrimaryError = failure;
                result = Result<object>.Fail(Errors.Problem(failure.Code, failure.Message));
            }
            return new BackgroundReadResult { Result = result, Lifetimes = binding.Capture() };
        }
        catch (Exception failure) when (CqrsBoundaryErrors.IsNonFatal(failure))
        {
            lifetime.PrimaryError = CapabilityWorkLifetime.CombinePrimary(lifetime.PrimaryError, failure);
            throw;
        }
        catch (Exception failure) when (!CqrsBoundaryErrors.IsNonFatal(failure))
        {
            lifetime.PrimaryError = CapabilityWorkLifetime.CombinePrimary(lifetime.PrimaryError, failure);
            throw;
        }
    }

    private static void Require(BackgroundReadBudget bounds, TimeProvider clock)
    {
        if (bounds is not null && bounds.ExpiresAt <= clock.GetUtcNow())
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        if (bounds is null || bounds.ExpiresAt.Offset != TimeSpan.Zero)
        {
            throw Errors.Fail(ErrorCode.Validation, GrainRoutingProtocol.InvalidRequest);
        }
    }
}
