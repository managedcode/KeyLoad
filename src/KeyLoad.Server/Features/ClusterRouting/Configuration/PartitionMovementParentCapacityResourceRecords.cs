using static KeyLoad.Server.Features.ClusterRouting.PartitionMovementParentCapacityPrimitives;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementParentCapacityResourceRecords
{
    internal static long Bound(QueuePolicy _)
        => checked(ReferenceFieldBytes
            + ScalarFieldBytes + ScalarFieldBytes
            + ScalarFieldBytes + ScalarFieldBytes
            + ScalarFieldBytes + ScalarFieldBytes
            + ScalarFieldBytes + ScalarFieldBytes);

    internal static long Bound(SensitiveFieldPolicy value)
        => checked(ReferenceFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(value.Path) + PartitionMovementParentCapacityBounds.Bound(value.Classification)
            + PartitionMovementParentCapacityBounds.Bound(value.RawReadGrant) + PartitionMovementParentCapacityBounds.Bound(value.RawUseGrant)
            + PartitionMovementParentCapacityBounds.Bound(value.WriteGrant) + ScalarFieldBytes);

    internal static long Bound(EventRetentionPolicy _)
        => checked(ReferenceFieldBytes
            + ScalarFieldBytes + ScalarFieldBytes);

    internal static long Bound(ResourceDefinition value)
        => checked(ReferenceFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(value.Name) + ScalarFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(value.TransactionDomainId) + PartitionMovementParentCapacityBounds.Bound(value.Indexes)
            + PartitionMovementParentCapacityBounds.Bound(value.FieldPolicies) + PartitionMovementParentCapacityBounds.Bound(value.HeaderPolicies)
            + PartitionMovementParentCapacityBounds.Bound(value.QueuePolicy) + PartitionMovementParentCapacityBounds.Bound(value.EventRetention)
            + ScalarFieldBytes + ScalarFieldBytes
            + ScalarFieldBytes + (value.BlobPolicy is { } presentvalueBlobPolicy ? PartitionMovementParentCapacityBounds.Bound(presentvalueBlobPolicy) : ReferenceFieldBytes)
            + (value.RelationalSchema is { } presentvalueRelationalSchema ? PartitionMovementParentCapacityBounds.Bound(presentvalueRelationalSchema) : ReferenceFieldBytes) + PartitionMovementParentCapacityBounds.Bound(value.VectorProfiles));

    internal static long Bound(PrincipalRecord value)
        => checked(ReferenceFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(value.Id) + PartitionMovementParentCapacityBounds.Bound(value.TenantId)
            + PartitionMovementParentCapacityBounds.Bound(value.Grants) + PartitionMovementParentCapacityBounds.Bound(value.FieldGrants)
            + ScalarFieldBytes + PartitionMovementParentCapacityBounds.Bound(value.OwnerId)
            + PartitionMovementParentCapacityBounds.Bound(value.Projects) + ScalarFieldBytes
            + ScalarFieldBytes + (value.ExpiresAt is not null ? DateTimeFieldBytes : ReferenceFieldBytes)
            + ScalarFieldBytes);

    internal static long Bound(VectorSpace value)
        => checked(ReferenceFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(value.Id) + ScalarFieldBytes
            + ScalarFieldBytes + PartitionMovementParentCapacityBounds.Bound(value.Model)
            + PartitionMovementParentCapacityBounds.Bound(value.Version));

    internal static long Bound(IndexDefinition value)
        => checked(ReferenceFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(value.Name) + PartitionMovementParentCapacityBounds.Bound(value.Fields)
            + ScalarFieldBytes + ScalarFieldBytes
            + ScalarFieldBytes);

    internal static long Bound(RelationalSchema value)
        => checked(ReferenceFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(value.PrimaryKey) + PartitionMovementParentCapacityBounds.Bound(value.Columns));

    internal static long Bound(BlobPolicy _)
        => checked(ReferenceFieldBytes
            + ScalarFieldBytes + ScalarFieldBytes
            + ScalarFieldBytes + ScalarFieldBytes
            + ScalarFieldBytes + ScalarFieldBytes);

    internal static long Bound(RelationalColumn value)
        => checked(ReferenceFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(value.Name) + ScalarFieldBytes
            + ScalarFieldBytes);

    internal static long Bound(ScopeGrant value)
        => checked(ReferenceFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(value.Database) + PartitionMovementParentCapacityBounds.Bound(value.Resource)
            + ScalarFieldBytes);

    internal static long Bound(VectorFieldProfile value)
        => checked(ReferenceFieldBytes
            + PartitionMovementParentCapacityBounds.Bound(value.Field) + PartitionMovementParentCapacityBounds.Bound(value.Space));
}
