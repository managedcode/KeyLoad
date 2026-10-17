using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution.Execution;

namespace KeyLoad.Replication;

/// <summary>Validates finite original-grant numeric contracts without creating or completing any work owner.</summary>
internal static class ReplicaForwardReadBudget
{
    internal const string GrantAlias = "keyload.replica.forward-read-grant.v1";
    internal const string StatsAlias = "keyload.replica.forward-read-stats.v1";
    private const int NoCapacity = 0;
    private const string InvalidGrant = "The replica forward metadata grant is invalid.";
    private const string InvalidStats = "The replica forward metadata statistics are invalid.";

    internal static ReplicaForwardReadGrantV1 Capture(ReadExecutionBudget work,
        ReadExecutionBudgetReadGrant grant)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(grant);
        work.Check();
        if (!grant.BelongsTo(work) || grant.IsCompleted)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidGrant);
        }
        var result = new ReplicaForwardReadGrantV1(grant.RemainingBytes,
            grant.RemainingRecords, work.MaximumResultBytes);
        if (!ValidCaps(result))
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, InvalidGrant);
        }
        return result;
    }

    internal static void RequireCaps(ReplicaForwardReadGrantV1 caps, DatabaseLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        if (!ValidCaps(caps) || caps.MaximumReadBytes > limits.MaxQueryReadBytes
            || caps.MaximumExaminedRecords > limits.MaxScanRecords
            || caps.MaximumResultBytes > limits.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidGrant);
        }
    }

    internal static void RequireStats(ReplicaForwardReadGrantV1 caps,
        ReplicaForwardReadStatsV1 stats, int actualResultPayloadBytes)
    {
        if (!ValidCaps(caps))
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidGrant);
        }
        if (stats is null || actualResultPayloadBytes < NoCapacity
            || stats.ReadBytes < NoCapacity || stats.ReadBytes > caps.MaximumReadBytes
            || stats.ExaminedRecords < NoCapacity || stats.ExaminedRecords > caps.MaximumExaminedRecords
            || stats.ResultBytes != actualResultPayloadBytes || stats.ResultBytes > caps.MaximumResultBytes)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidStats);
        }
    }

    private static bool ValidCaps(ReplicaForwardReadGrantV1 caps)
        => caps is not null && caps.MaximumReadBytes > NoCapacity
            && caps.MaximumExaminedRecords > NoCapacity && caps.MaximumResultBytes > NoCapacity;
}
