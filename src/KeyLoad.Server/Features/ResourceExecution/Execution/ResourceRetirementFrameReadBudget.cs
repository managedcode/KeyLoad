using KeyLoad.Clustering;
using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ResourceExecution;

/// <summary>Captures only the admitted frame's original caps, expiry and cancellation before any owner read.</summary>
internal static class ResourceRetirementFrameReadBudget
{
    internal static ReadExecutionBudget Create(ResourceRetirementReadRequestV1 query,
        IOptions<DatabaseLimits> limits, TimeProvider clock, CancellationToken token)
    {
        if (query is null)
        {
            throw Errors.Fail(ErrorCode.Validation, ResourceRetirementNativeProtocol.InvalidProof);
        }

        return Create(query.MaximumReadBytes, query.MaximumExaminedRecords, query.MaximumResultBytes,
            query.ExpiresAt, limits, clock, token);
    }

    internal static ReadExecutionBudget Create(long bytes, int records, int results, DateTimeOffset expiry,
        IOptions<DatabaseLimits> limits, TimeProvider clock, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (bytes <= ResourceRetirementReadContract.NoReadBytes || bytes > limits.Value.MaxQueryReadBytes
            || records <= ResourceRetirementReadContract.NoRecords || records > limits.Value.MaxScanRecords
            || results <= ResourceRetirementReadContract.NoRecords || results > limits.Value.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.Validation, ResourceRetirementNativeProtocol.InvalidProof);
        }

        if (expiry <= clock.GetUtcNow())
        {
            throw Errors.Fail(ErrorCode.CursorExpired, ResourceRetirementNativeProtocol.Unavailable);
        }

        var work = new ReadExecutionBudget(limits, clock, token);
        work.ConstrainLifetime(expiry);
        work.ConstrainNativeWork(bytes, records);
        work.ConstrainResultBytes(results);
        return work;
    }

    internal static void RequireAvailable(ReadExecutionBudget work)
    {
        work.Check();
        if (work.RemainingReadGrantBytes <= ResourceRetirementReadContract.NoReadBytes
            || work.RemainingReadGrantRecords <= ResourceRetirementReadContract.NoRecords)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ResourceRetirementNativeProtocol.Unavailable);
        }
    }

    internal static ResourceRetirementReadReplyV1 Complete(ResourceRetirementReadReplyV1 reply, ReadExecutionBudget work)
    {
        var result = reply with
        {
            ReadBytes = work.ReadBytes,
            ExaminedRecords = work.ConstrainedNativeRecords
        };
        work.CheckResult(result);
        work.Check();
        return result;
    }
}
