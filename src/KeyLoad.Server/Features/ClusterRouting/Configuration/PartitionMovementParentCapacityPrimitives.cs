using System.Text;
using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementParentCapacityPrimitives
{
    internal const long ReferenceFieldBytes = 16;
    internal const long ScalarFieldBytes = 21;
    internal const long GuidFieldBytes = 32;
    internal const long DateTimeFieldBytes = 58;
    internal const long MaximumLengthPrefixBytes = 5;

    internal static long Bytes(long length)
        => checked(ReferenceFieldBytes + MaximumLengthPrefixBytes + length);

    internal static long Bound(string? value)
        => value is null ? ReferenceFieldBytes : Bytes(Encoding.UTF8.GetByteCount(value));

    // Each known polymorphic value is bounded independently; exact native root encoding
    // additionally charges its actual encoded type framing, without assuming alias reuse.

    internal static long BoundNativeValue(object value)
        => value switch
        {
            PartitionMovePhaseResult phase => checked(PartitionMovementParentCapacityBounds.Bound(phase) + NativeSerialization.Measure(phase)),
            PartitionMoveResult result => checked(PartitionMovementParentCapacityBounds.Bound(result) + NativeSerialization.Measure(result)),
            CommitReceipt receipt => checked(PartitionMovementParentCapacityBounds.Bound(receipt) + NativeSerialization.Measure(receipt)),
            PartitionMoveExpiredRetireCancellation cancellation
                => checked(PartitionMovementParentCapacityBounds.Bound(cancellation) + NativeSerialization.Measure(cancellation)),
            _ => throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity)
        };
}
