using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class D2ResourceInventoryData
{
    internal const string Root = "root";
    internal const string Administrator = "d2-inventory-administrator";
    internal const string Tenant = "d2-tenant";
    internal const string Database = "d2-database";
    internal const string Domain = "d2-orders";
    internal const string OtherDomain = "d2-unused";
    internal const string A = "d2-documents-a";
    internal const string B = "d2-documents-b";
    internal const string C = "d2-documents-c";
    private const string FirstPartitionKey = "p0";
    private const string SecondPartitionKey = "p1";
    private const string ThirdPartitionKey = "p2";
    internal const string DocumentId = "document";
    internal const int Version = 1;
    internal const int PageSize = 1;
    internal const int EmptyRevision = 0;
    internal const int InitialRevision = 1;
    internal const int TwoColdOwners = 2;
    internal const int InitialEpoch = 1;
    internal const int DemotedEpoch = 2;
    internal const int RepairedEpoch = 3;
    internal const int MinimalBudget = 1;
    internal const int InvalidPageSize = 0;
    internal const int BeforeExpiryTick = -1;
    internal static readonly PartitionRef[] Partitions =
    [
        new(Tenant, Database, Domain, FirstPartitionKey),
        new(Tenant, Database, Domain, SecondPartitionKey),
        new(Tenant, Database, Domain, ThirdPartitionKey)
    ];
    internal static readonly string[] Json = ["{\"value\":0}", "{\"value\":1}", "{\"value\":2}"];
    internal static readonly string[] Collections = [A, A, B];
    internal static RegisteredPhysicalOwnerV1 Control => PhysicalOwnerDirectoryWholeFlow.Control;
    internal static RegisteredPhysicalOwnerV1 Destination => PhysicalOwnerDirectoryWholeFlow.Destination;
}
