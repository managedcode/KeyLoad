namespace KeyLoad.Core.Features.Search;

internal static class AnnSeedScopeCapture
{
    internal static AnnSeedScope Capture(AnnSeedBuffer buffer, PrincipalRecord principal, PartitionRef partition,
        string collection, string field, VectorSpace space, long schemaVersion, DateTimeOffset evaluatedAt)
    {
        var principalId = buffer.CopyString(principal.Id);
        var tenantId = buffer.CopyString(partition.TenantId);
        var databaseId = buffer.CopyString(partition.DatabaseId);
        var domainId = buffer.CopyString(partition.TransactionDomainId);
        var partitionKey = buffer.CopyString(partition.PartitionKey);
        var ownedPartition = new PartitionRef(tenantId, databaseId, domainId, partitionKey);
        var ownedCollection = buffer.CopyString(collection);
        var ownedField = buffer.CopyString(field);
        var ownedSpace = new VectorSpace(buffer.CopyString(space.Id), space.Dimension, space.Metric,
            buffer.CopyString(space.Model), buffer.CopyString(space.Version));
        return new(principalId, principal.PolicyEpoch, ownedPartition, ownedCollection,
            ownedField, schemaVersion, ownedSpace, evaluatedAt);
    }
}
