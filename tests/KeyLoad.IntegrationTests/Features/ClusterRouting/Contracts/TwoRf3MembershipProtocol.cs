namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TwoRf3MembershipProtocol
{
    internal const string ProfileArgument = "--KeyLoadTests:ClusterRouting:Profile=two-rf3";
    internal const string EphemeralArgument = "--KeyLoad:Ephemeral=true";
    internal const string DataRootPrefix = "--KeyLoad:DataRoot=";
    internal const string Node1 = "node1";
    internal const string Node2 = "node2";
    internal const string Node3 = "node3";
    internal const string Node4 = "node4";
    internal const string Node5 = "node5";
    internal const string Node6 = "node6";
    internal const string HealthSilo = "health/silo";
    internal const string HealthReady = "health/ready";
    internal const string HealthAuthority = "health/membership-authority";
    internal const string HealthMembership = "health/membership-ready";
    internal const string ImageMismatch = "The two-RF3 model did not bind all six resources to the verified immutable server image.";
    internal const string MissingState = "The two-RF3 AppHost did not reach its expected membership-only state.";
    internal const string InvalidRoot = "The owned two-RF3 data root could not be safely removed after full disposal.";
    internal static readonly TimeSpan CleanupDeadline = TimeSpan.FromSeconds(60);
    internal const int NodeCount = 6;
    internal const int MembersPerGroup = 3;
    internal static readonly string[] Nodes = [Node1, Node2, Node3, Node4, Node5, Node6];
}
