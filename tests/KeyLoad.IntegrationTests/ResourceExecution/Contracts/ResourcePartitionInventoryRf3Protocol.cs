namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal static class ResourcePartitionInventoryRf3Protocol
{
    internal const string Tenant = "protected-parent-tenant";
    internal const string Database = "d2-inventory-database";
    internal const string Domain = "d2-inventory-domain";
    internal const string OtherDomain = "d2-inventory-unused-domain";
    internal const string FirstPartitionKey = "p0";
    internal const string SecondPartitionKey = "p1";
    internal const string ThirdPartitionKey = "p2";
    internal const string First = "d2-inventory-a";
    internal const string Second = "d2-inventory-b";
    internal const string Unused = "d2-inventory-unused";
    internal const string LateId = "rolled-back";
    internal const string LateJson = "{\"private\":\"d2-inventory-private-payload\",\"value\":99}";
    internal const string PrivateMarker = "d2-inventory-private-payload";
    internal const string RevisionDetail = "The expected revision does not match.";
    internal const int ColdCount = 2;
    internal const long InitialRevision = 1L;
    internal const long MissingRevision = 0L;
    internal const string PutKind = "putDocument";
    internal static PartitionRef Partition(string key) => new(Tenant, Database, Domain, key);
    internal static ResourceDefinition Definition(string name, string domain)
        => new(name, ResourceKind.Collection, domain);
    internal static ConfigureResourceRequest Configure(ResourceDefinition definition)
        => new(Tenant, Database, definition);
}
