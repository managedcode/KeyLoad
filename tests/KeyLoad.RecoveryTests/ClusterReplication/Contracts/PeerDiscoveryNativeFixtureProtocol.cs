namespace KeyLoad.RecoveryTests;

internal static class PeerDiscoveryNativeFixtureProtocol
{
    internal const string DirectoryPrefix = "keyload-native-recovery-discovery-";
    internal const string ReplicaDirectory = "replica";
    internal const string Cohort = "recovery-native-discovery";
    internal const int NoFailures = 0;
    internal static readonly TimeSpan ConnectTimeout = TimeSpan.FromMilliseconds(200);
}
