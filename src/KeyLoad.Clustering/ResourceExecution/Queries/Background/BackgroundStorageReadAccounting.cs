using KeyLoad.Core;
using KeyLoad.Core.ResourceExecution;

namespace KeyLoad.Clustering;

/// <summary>Binds actual owner reads to the same original producer ledger.</summary>
internal static class BackgroundStorageReadAccounting
{
    internal static async Task<T> ReadAsync<T>(ReadExecutionBudget work, IResourceLifetimeWorkBinding binding,
        Func<CancellationToken, Task<BackgroundReadResult>> send,
        bool allowAbsent = false, CancellationToken token = default)
    {
        work.Check();
        var failures = new List<Exception>();
        var dispatched = false;
        var imported = false;
        T? result = default;
        try
        {
            dispatched = true;
            var response = await send(token);
            Require(response);
            binding.Bind(response.Lifetimes);
            imported = true;
            if ((response.Result /* KLD0041: Validate the existing generated background Result<object> and GrainValue slots before publishing the concrete caller result with its original lifetime accounting. */).IsFailed)
            {
                throw Errors.Fail(Errors.Code((response.Result /* KLD0041: Validate the existing generated background Result<object> and GrainValue slots before publishing the concrete caller result with its original lifetime accounting. */).Problem).Value!.Value, (response.Result /* KLD0041: Validate the existing generated background Result<object> and GrainValue slots before publishing the concrete caller result with its original lifetime accounting. */).Problem!.Detail!);
            }
            work.Check();
            result = (ReadValue<T>(((response.Result /* KLD0041: Validate the existing generated background Result<object> and GrainValue slots before publishing the concrete caller result with its original lifetime accounting. */).Value /* KLD0041: Validate the existing generated background Result<object> and GrainValue slots before publishing the concrete caller result with its original lifetime accounting. */), allowAbsent) /* KLD0041: Validate the existing generated background Result<object> and GrainValue slots before publishing the concrete caller result with its original lifetime accounting. */);
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

    private static T? ReadValue<T>(object? /* KLD0041: Validate the existing generated background Result<object> and GrainValue slots before publishing the concrete caller result with its original lifetime accounting. */ result, bool allowAbsent)
    {
        if ((result is GrainValue boxed /* KLD0042: Extract KeyLoad.Clustering.GrainValue from the current generated background-read result slots before binding the concrete result to its original lifetime ledger. */))
        {
            if (((boxed.Value /* KLD0041: Read the current generated GrainValue field only to validate the concrete RPC response or its permitted absence. */) is T actual /* KLD0042: Extract T from the current generated background-read result slots before binding the concrete result to its original lifetime ledger. */))
            {
                return actual;
            }
            if (allowAbsent && (boxed.Value /* KLD0041: Read the current generated GrainValue field only to validate the concrete RPC response or its permitted absence. */) is null)
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
            || (response.Result /* KLD0041: Validate the existing generated background Result<object> and GrainValue slots before publishing the concrete caller result with its original lifetime accounting. */).IsFailed && (Errors.Code((response.Result /* KLD0041: Validate the existing generated background Result<object> and GrainValue slots before publishing the concrete caller result with its original lifetime accounting. */).Problem).Value is null
                || string.IsNullOrEmpty((response.Result /* KLD0041: Validate the existing generated background Result<object> and GrainValue slots before publishing the concrete caller result with its original lifetime accounting. */).Problem.Detail))
            || (response.Result /* KLD0041: Validate the existing generated background Result<object> and GrainValue slots before publishing the concrete caller result with its original lifetime accounting. */).IsSuccess && (response.Result /* KLD0041: Validate the existing generated background Result<object> and GrainValue slots before publishing the concrete caller result with its original lifetime accounting. */).Problem is not null)
        {
            throw Errors.Fail(ErrorCode.Corruption, GrainRoutingProtocol.InvalidRequest);
        }
    }
}
