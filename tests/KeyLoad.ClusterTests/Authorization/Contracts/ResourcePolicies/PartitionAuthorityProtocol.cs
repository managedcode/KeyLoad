
namespace KeyLoad.ClusterTests.Authorization;

internal static class PartitionAuthorityProtocol
{
    internal const long RepairEpoch = 2;
    internal const long ReducedEpoch = 3;
    internal const long RevokedEpoch = 4;
    internal const long FinalEpoch = 5;
    internal const long Schema = 1;
    internal const int FirstLeaf = 0;
    internal const int SecondLeaf = 1;
    internal const int LeafCount = 2;
    internal const string FirstId = "authorized-row";
    internal const string SecondId = "denied-row";
    internal const string FirstJson = "{\"rank\":1,\"value\":\"authorized\"}";
    internal const string SecondJson = "{\"rank\":1,\"value\":\"denied\"}";
    internal const string FirstProjection = "{\"value\":\"authorized\"}";
    internal const string SecondProjection = "{\"value\":\"denied\"}";
    internal const string FirstValue = "authorized";
    internal const string SecondValue = "denied";
    internal const string FullScan = "bounded-full-scan";
    internal const string ColdScenario = "partition-query-authority-cold";
}
