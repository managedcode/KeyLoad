namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class NativeBoundLeaseProtocol
{
    internal const string FirstId = "bound-lease-first";
    internal const string SecondId = "bound-lease-second";
    internal const string FirstJson = "{\"value\":\"first-literal\"}";
    internal const string SecondJson = "{\"value\":\"second-literal\"}";
    internal const string Projection = "*";
    internal const string ProjectionAlias = "document";
    internal const string MutationKind = "putDocument";
    internal const string Missing = "The native bound lease value is unavailable.";
    internal const string ImageLimit = "Whole store state exceeded the fixture bound.";
    internal const string RecordSeparator = ":";
    internal const int Version = 1;
    internal const int Revision = 1;
    internal const int RowCount = 2;
    internal const int LifetimeCount = 1;
    internal const int PersistedCopies = 1;
    internal const int NextIndex = 1;
    internal const int GrantDivisor = 4;
    internal const int MaximumImageRecords = 4096;
    internal const int Empty = 0;
    internal const string Root = "root";
    internal const string Collection = "bound-lease-documents";
    internal static readonly PartitionRef Partition = new(RequestCqrsHostedProtocol.Tenant,
        RequestCqrsHostedProtocol.Database, RequestCqrsHostedProtocol.Domain, RequestCqrsHostedProtocol.Partition);
}
