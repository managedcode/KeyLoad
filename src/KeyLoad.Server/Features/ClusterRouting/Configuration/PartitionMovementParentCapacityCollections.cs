using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using static KeyLoad.Server.Features.ClusterRouting.PartitionMovementParentCapacityPrimitives;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementParentCapacityCollections
{
    internal static long Bound(ImmutableArray<IndexDefinition> values)
    {
        if (values.IsDefault)
        { return ReferenceFieldBytes + ReferenceFieldBytes; }
        var bytes = checked(ReferenceFieldBytes + ReferenceFieldBytes + ScalarFieldBytes);
        foreach (var value in values)
        { bytes = checked(bytes + PartitionMovementParentCapacityBounds.Bound(value)); }
        return bytes;
    }

    internal static long Bound(ImmutableArray<RelationalColumn> values)
    {
        if (values.IsDefault)
        { return ReferenceFieldBytes + ReferenceFieldBytes; }
        var bytes = checked(ReferenceFieldBytes + ReferenceFieldBytes + ScalarFieldBytes);
        foreach (var value in values)
        { bytes = checked(bytes + PartitionMovementParentCapacityBounds.Bound(value)); }
        return bytes;
    }

    internal static long Bound(ImmutableArray<VectorFieldProfile> values)
    {
        if (values.IsDefault)
        { return ReferenceFieldBytes + ReferenceFieldBytes; }
        var bytes = checked(ReferenceFieldBytes + ReferenceFieldBytes + ScalarFieldBytes);
        foreach (var value in values)
        { bytes = checked(bytes + PartitionMovementParentCapacityBounds.Bound(value)); }
        return bytes;
    }

    internal static long Bound(ImmutableArray<PartitionMoveImageFamily> values)
    {
        if (values.IsDefault)
        { return ReferenceFieldBytes + ReferenceFieldBytes; }
        var bytes = checked(ReferenceFieldBytes + ReferenceFieldBytes + ScalarFieldBytes);
        foreach (var value in values)
        { bytes = checked(bytes + PartitionMovementParentCapacityBounds.Bound(value)); }
        return bytes;
    }

    internal static long Bound(ImmutableArray<ScopeGrant> values)
    {
        if (values.IsDefault)
        { return ReferenceFieldBytes + ReferenceFieldBytes; }
        var bytes = checked(ReferenceFieldBytes + ReferenceFieldBytes + ScalarFieldBytes);
        foreach (var value in values)
        { bytes = checked(bytes + PartitionMovementParentCapacityBounds.Bound(value)); }
        return bytes;
    }

    internal static long Bound(ImmutableArray<RegisteredPhysicalOwnerV1> values)
    {
        if (values.IsDefault)
        { return ReferenceFieldBytes + ReferenceFieldBytes; }
        var bytes = checked(ReferenceFieldBytes + ReferenceFieldBytes + ScalarFieldBytes);
        foreach (var value in values)
        { bytes = checked(bytes + PartitionMovementParentCapacityBounds.Bound(value)); }
        return bytes;
    }

    internal static long Bound(ImmutableArray<SensitiveFieldPolicy> values)
    {
        if (values.IsDefault)
        { return ReferenceFieldBytes + ReferenceFieldBytes; }
        var bytes = checked(ReferenceFieldBytes + ReferenceFieldBytes + ScalarFieldBytes);
        foreach (var value in values)
        { bytes = checked(bytes + PartitionMovementParentCapacityBounds.Bound(value)); }
        return bytes;
    }

    internal static long Bound(ImmutableArray<MutationReceipt> values)
    {
        if (values.IsDefault)
        { return ReferenceFieldBytes + ReferenceFieldBytes; }
        var bytes = checked(ReferenceFieldBytes + ReferenceFieldBytes + ScalarFieldBytes);
        foreach (var value in values)
        { bytes = checked(bytes + PartitionMovementParentCapacityBounds.Bound(value)); }
        return bytes;
    }

    internal static long Bound(ImmutableArray<ResourceDefinition> values)
    {
        if (values.IsDefault)
        { return ReferenceFieldBytes + ReferenceFieldBytes; }
        var bytes = checked(ReferenceFieldBytes + ReferenceFieldBytes + ScalarFieldBytes);
        foreach (var value in values)
        { bytes = checked(bytes + PartitionMovementParentCapacityBounds.Bound(value)); }
        return bytes;
    }

    internal static long Bound(ImmutableArray<string> values)
    {
        if (values.IsDefault)
        { return ReferenceFieldBytes + ReferenceFieldBytes; }
        var bytes = checked(ReferenceFieldBytes + ReferenceFieldBytes + ScalarFieldBytes);
        foreach (var value in values)
        { bytes = checked(bytes + PartitionMovementParentCapacityBounds.Bound(value)); }
        return bytes;
    }
}
