namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class NativeActivationRf3Protocol
{
    internal const string Missing = "The actual native database activation witness is absent.";
    internal const string Replacement = "native-database-activation-replacement";
    internal const string Cold = "native-activation-all-owner-cold-cut";
    internal const int CommittedRevision = 2;
    internal static readonly TimeSpan ObservationInterval = TimeSpan.FromMilliseconds(100);
}
