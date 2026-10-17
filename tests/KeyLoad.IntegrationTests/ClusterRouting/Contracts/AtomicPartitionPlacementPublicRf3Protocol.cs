namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class AtomicPartitionPlacementPublicRf3Protocol
{
    internal const string BindTool = "keyload_admin_partition_placement_bind";
    internal const string ReadTool = "keyload_admin_partition_placement_read";
    internal const string BindRoute = "/v1/admin/partition-placement/bind";
    internal const string ReadRoute = "/v1/admin/partition-placement/read";
    internal const string RestartScenario = "atomic-partition-placement-voter-reopen";
    internal const string Tenant = "pmap-rf3-tenant";
    internal const string Database = "pmap-rf3-database";
    internal const string Domain = "pmap-rf3-domain";
    internal const string PartitionId = "pmap-rf3-bound";
    internal const string FallbackPartitionId = "pmap-rf3-fallback";
    internal const string PrincipalResource = "pmap-rf3-principals";
    internal const string CredentialResource = "pmap-rf3-credentials";
    internal const string CommandHeader = "commandId";
    internal static readonly string[] Nodes =
    [RequestCqrsRf3Protocol.Node1, RequestCqrsRf3Protocol.Node2, RequestCqrsRf3Protocol.Node3];
}
