using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Server;

/// <summary>Reserves modeled immutable-image construction and retained state before native capture.</summary>
internal static class PartitionMovementImageReservation
{
    private const int EmptyBytes = 0;
    private const int ConstructionRawCopies = 2;
    private const int RecordAndPageMetadataBytes = 512;
    private const int FamilyMetadataBytes = 512;
    private const int SessionMetadataBytes = 4096;
    private const string CapacityDetail = "The partition movement image exceeds its bounded retention capacity.";

    internal static ICacheMemoryReservation Acquire(ICacheMemoryBudget memory, DatabaseLimits limits,
        long maximumImageBytes, int maximumRecords, long resourceMetadataBytes)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(limits);
        if (maximumImageBytes <= EmptyBytes || maximumImageBytes > limits.MaxQueryReadBytes
            || maximumRecords <= EmptyBytes || maximumRecords > limits.MaxScanRecords
            || resourceMetadataBytes < EmptyBytes || resourceMetadataBytes > limits.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, CapacityDetail); }
        long charge;
        try
        {
            charge = checked(maximumImageBytes * ConstructionRawCopies
                + (long)maximumRecords * RecordAndPageMetadataBytes
                + (long)PartitionRecordFamilies.All.Length * FamilyMetadataBytes
                + resourceMetadataBytes * ConstructionRawCopies + SessionMetadataBytes);
        }
        catch (OverflowException)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, CapacityDetail); }
        if (!memory.TryReserve(charge, maximumRecords, out var reservation) || reservation is null)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, CapacityDetail); }
        return reservation;
    }
}
