using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using ManagedCode.Communication;

namespace KeyLoad.Server;

/// <summary>Settles actual physical read work before returning its bounded accounting to the original caller.</summary>
internal static class BackgroundStorageReadExecution
{
    internal static async Task<T> RunLocalAsync<T>(PartitionStorageScope storage,
        Func<CancellationToken, T> read, CancellationToken token)
    {
        await using var lifetime = CapabilityWorkLifetime.Acquire(storage.Work, Guid.NewGuid(),
            RequestWorkKind.RuntimeTransport, storage.Host.Database.OperationLimitsOptions, token)
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
        var database = storage.Host.Database;
        Require(bounds, database.Limits, clock);
        await using var lifetime = CapabilityWorkLifetime.Acquire(storage.Work, Guid.NewGuid(),
            RequestWorkKind.RuntimeTransport, database.OperationLimitsOptions, token)
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        var work = new ReadExecutionBudget(database.OperationLimitsOptions, clock, lifetime.Token);
        work.ConstrainLifetime(bounds.ExpiresAt);
        work.ConstrainWork(bounds.MaximumReadBytes, bounds.MaximumRecords);
        work.ConstrainResultBytes(bounds.MaximumResultBytes);
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
            var response = new BackgroundReadResult(result, work.ReadBytes, work.ConstrainedRecords,
                binding.Capture(bounds.MaximumResultBytes));
            if (OrleansSerialization.Measure(response) > bounds.MaximumResultBytes)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, GrainRoutingProtocol.ReplyBudgetExceeded);
            }
            return response;
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

    private static void Require(BackgroundReadBudget bounds, DatabaseLimits limits, TimeProvider clock)
    {
        if (bounds is not null && bounds.ExpiresAt <= clock.GetUtcNow())
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        if (bounds is null || bounds.ExpiresAt.Offset != TimeSpan.Zero
            || bounds.MaximumReadBytes <= default(long) || bounds.MaximumReadBytes > limits.MaxQueryReadBytes
            || bounds.MaximumRecords <= default(int) || bounds.MaximumRecords > limits.MaxScanRecords
            || bounds.MaximumResultBytes <= default(int) || bounds.MaximumResultBytes > limits.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.Validation, GrainRoutingProtocol.InvalidRequest);
        }
    }
}
