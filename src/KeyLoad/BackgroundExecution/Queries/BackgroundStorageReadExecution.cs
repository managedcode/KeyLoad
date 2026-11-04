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
        TimeProvider clock,
        Func<ReadExecutionBudget, IResourceLifetimeWorkBinding, CancellationToken, Task<T>> execute,
        CancellationToken token)
    {
        await using var lifetime = (CapabilityWorkLifetime.Acquire(storage.Work, Guid.NewGuid(),
            RequestWorkKind.RuntimeTransport, token)).Value
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        var work = new ReadExecutionBudget(clock, lifetime.Token);
        var binding = new BackgroundStorageReadBinding(lifetime);
        work.AttachResourceLifetimeBinding(binding);
        try
        {
            Result<object> /* KLD0041: Preserve the existing generated BackgroundReadResult.Result field and native mixed success/error shape while retaining resource lifetimes with the reply. */ result;
            try
            {
                var value = await execute(work, binding, lifetime.Token);
                result = (Result<object>.Succeed((new GrainValue((value /* KLD0042: Erase the typed read result only into the existing generated GrainValue wire slot; scalar reads require the current runtime type header. */)) /* KLD0041: Create the existing generated GrainValue carrier exactly once for this typed physical read result at the native reply boundary. */)) /* KLD0041: Store the existing GrainValue reply carrier in the current generated BackgroundReadResult.Result success slot. */);
                work.Check();
            }
            catch (KeyLoadException failure)
            {
                lifetime.PrimaryError = failure;
                result = (Result<object>.Fail(Errors.Problem(failure.Code, failure.Message)) /* KLD0041: Store the typed problem in the existing generated BackgroundReadResult.Result error shape; there is no successful object payload on this branch. */);
            }
            return new BackgroundReadResult { Result /* KLD0041: Assign the current generated BackgroundReadResult.Result field without altering its stable serializer identity or lifetime receipt shape. */ = result, Lifetimes = binding.Capture() };
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
}
