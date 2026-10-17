namespace KeyLoad.UnitTests;

internal static class TestDatabaseResourceConfiguration
{
    internal static ResourceDefinition Apply(TestDatabase database, string name, ResourceKind kind, string? domain,
        IndexDefinition[]? indexes, SensitiveFieldPolicy[]? fields, QueuePolicy? queuePolicy)
    {
        var resource = new ResourceDefinition(name, kind, domain ?? database.Partition.TransactionDomainId)
        {
            Indexes = [.. indexes ?? []],
            FieldPolicies = [.. fields ?? []],
            QueuePolicy = queuePolicy ?? new()
        };
        return database.Submit(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(database.Partition.TenantId, database.Partition.DatabaseId, resource)).Get<ResourceDefinition>();
    }
}
