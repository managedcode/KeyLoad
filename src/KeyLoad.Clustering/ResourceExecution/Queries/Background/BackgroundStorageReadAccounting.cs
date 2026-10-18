using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Clustering;

/// <summary>Charges actual owner reads to the same original producer ledger without renewing its limits.</summary>
internal static class BackgroundStorageReadAccounting
{
    internal static async Task<T> ReadAsync<T>(ReadExecutionBudget work, IResourceLifetimeWorkBinding binding,
        DateTimeOffset expiry, Func<BackgroundReadBudget, CancellationToken, Task<BackgroundReadResult>> send,
        bool allowAbsent = false, CancellationToken token = default)
    {
        work.Check();
        var bytes = work.RemainingReadGrantBytes;
        var records = work.RemainingReadGrantRecords;
        if (bytes <= default(long) || records <= default(int))
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ReadExecutionBudget.ReadBytesExceeded);
        }
        var bounds = new BackgroundReadBudget(expiry, bytes, records, work.MaximumResultBytes);
        var grant = work.CreateReadGrant(bytes, records);
        var failures = new List<Exception>();
        var dispatched = false;
        var imported = false;
        T? result = default;
        try
        {
            dispatched = true;
            var response = await send(bounds, token).ConfigureAwait(true);
            Require(response, bounds);
            var lifetimes = NativeSerialization.Deserialize<ImmutableArray<ResourceLifetimeV1>>(response.Lifetimes.Span);
            work.ImportSettledReadGrant(grant, response.ReadBytes, response.ExaminedRecords);
            binding.Bind(lifetimes);
            imported = true;
            if (response.Result.IsFailed)
            {
                throw Errors.Fail(Errors.Code(response.Result.Problem)!.Value, response.Result.Problem!.Detail!);
            }
            work.Check();
            result = ReadValue<T>(response.Result.Value, allowAbsent);
        }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            Record(work, failure, failures, dispatched, imported);
        }
        catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            Record(work, failure, failures, dispatched, imported);
        }
        try
        { work.CompleteSettledReadGrant(grant); }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
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

    private static void Require(BackgroundReadResult response, BackgroundReadBudget bounds)
    {
        if (response is null || response.ReadBytes < default(long) || response.ExaminedRecords < default(int)
            || response.ReadBytes > bounds.MaximumReadBytes || response.ExaminedRecords > bounds.MaximumRecords
            || response.Lifetimes.IsEmpty || response.Lifetimes.Length > bounds.MaximumResultBytes
            || response.Result.IsFailed && (Errors.Code(response.Result.Problem) is null
                || string.IsNullOrEmpty(response.Result.Problem?.Detail))
            || response.Result.IsSuccess && response.Result.Problem is not null
            || NativeSerialization.Measure(response) > bounds.MaximumResultBytes)
        {
            throw Errors.Fail(ErrorCode.Corruption, GrainRoutingProtocol.InvalidRequest);
        }
    }
}
