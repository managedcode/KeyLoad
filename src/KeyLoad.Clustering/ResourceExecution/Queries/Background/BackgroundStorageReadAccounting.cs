using KeyLoad.Core;
using KeyLoad.Core.ResourceExecution;

namespace KeyLoad.Clustering;

/// <summary>Binds actual owner reads to the same original producer ledger.</summary>
internal static class BackgroundStorageReadAccounting
{
    internal static async Task<T> ReadAsync<T>(ReadExecutionBudget work, IResourceLifetimeWorkBinding binding,
        DateTimeOffset expiry, Func<BackgroundReadBudget, CancellationToken, Task<BackgroundReadResult>> send,
        bool allowAbsent = false, CancellationToken token = default)
    {
        work.Check();
        var bounds = new BackgroundReadBudget(expiry);
        var failures = new List<Exception>();
        var dispatched = false;
        var imported = false;
        T? result = default;
        try
        {
            dispatched = true;
            var response = await send(bounds, token).ConfigureAwait(true);
            Require(response);
            binding.Bind(response.Lifetimes);
            imported = true;
            if (response.Result.IsFailed)
            {
                throw Errors.Fail(Errors.Code(response.Result.Problem).Value!.Value, response.Result.Problem!.Detail!);
            }
            work.Check();
            result = ReadValue<T>(response.Result.Value, allowAbsent);
        }
        catch (Exception failure) when (CqrsBoundaryErrors.IsNonFatal(failure))
        {
            Record(work, failure, failures, dispatched, imported);
        }
        catch (Exception failure) when (!CqrsBoundaryErrors.IsNonFatal(failure))
        {
            Record(work, failure, failures, dispatched, imported);
        }
        AnnProjectionJobFailures.ThrowIfAny(failures);
        return result!;
    }

    private static T? ReadValue<T>(object? result, bool allowAbsent)
    {
        if (result is GrainValue boxed)
        {
            if (boxed.Value is T actual)
            {
                return actual;
            }
            if (allowAbsent && boxed.Value is null)
            {
                return default;
            }
        }
        throw Errors.Fail(ErrorCode.Corruption, GrainRoutingProtocol.InvalidRequest);
    }

    private static void Record(ReadExecutionBudget work, Exception failure, List<Exception> failures,
        bool dispatched, bool imported)
    {
        if (dispatched && !imported)
        {
            work.FailIndeterminateMetadata(failure);
        }
        failures.Add(failure);
    }

    private static void Require(BackgroundReadResult response)
    {
        if (response is null || response.Lifetimes.IsDefault
            || response.Result.IsFailed && (Errors.Code(response.Result.Problem).Value is null
                || string.IsNullOrEmpty(response.Result.Problem.Detail))
            || response.Result.IsSuccess && response.Result.Problem is not null)
        {
            throw Errors.Fail(ErrorCode.Corruption, GrainRoutingProtocol.InvalidRequest);
        }
    }
}
