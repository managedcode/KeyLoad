
namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class NativeLocalChildLifetimeProtocol
{
    internal const string OtherSubject = "local-child-other";
    internal const string FirstId = "local-child-first";
    internal const string SecondId = "local-child-second";
    internal const string FirstJson = "{\"value\":\"first-literal\"}";
    internal const string SecondJson = "{\"value\":\"second-literal\"}";
    internal const string Projection = "*";
    internal const string ProjectionAlias = "document";
    internal const string MutationKind = "putDocument";
    internal const string Missing = "The native local-child fixture value is unavailable.";
    internal const int Version = 1;
    internal const int Revision = 1;
    internal const int RowCount = 2;
    internal const int ChangedLimit = 1;
    internal const int LifetimeCount = 1;
    internal const int NextIndex = 1;
    internal const int GrantDivisor = 4;
    internal const int Empty = 0;
    internal const string Root = "root";
    internal const string Collection = "local-child-documents";
    internal static readonly PartitionRef Partition = new(RequestCqrsHostedProtocol.Tenant,
        RequestCqrsHostedProtocol.Database, RequestCqrsHostedProtocol.Domain, RequestCqrsHostedProtocol.Partition);
}
