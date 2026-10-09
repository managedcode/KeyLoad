namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class MovementFrameObservationFixtureProtocol
{
    internal const string RootPrefix = "keyload-movement-frame-observation-";
    internal const string ProofSuffix = "-evidence";
    internal const string SessionFormat = "N";
    internal const string EnabledArgument = "--KeyLoadTests:MovementFrameObservation:Enabled=true";
    internal const string RootArgument = "--KeyLoadTests:MovementFrameObservation:Root=";
    internal const string SessionArgument = "--KeyLoadTests:MovementFrameObservation:SessionId=";
    internal const int FirstReceiver = TwoRf3MembershipProtocol.MembersPerGroup;
    internal const int InventoryOverflowStep = 1;
    internal const long InitialAggregateBytes = 0;
    internal const string NodeOwnerLock = "node.owner.lock";
    internal const string ReplicaDirectory = "replica";
    internal const string StoreLock = "owner.lock";
    internal const string Invalid = "The actual native final-frame observation did not match its owned cohort.";
    internal static IReadOnlyList<string> ReceiverNodes { get; } = Array.AsReadOnly(
        TwoRf3MembershipProtocol.Nodes[FirstReceiver..]);
}
