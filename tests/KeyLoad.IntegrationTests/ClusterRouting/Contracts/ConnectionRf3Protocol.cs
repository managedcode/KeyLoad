namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class ConnectionRf3Protocol
{
    internal const string Http2Endpoint = "http2";
    internal const string Isolation = "connection-native-rf3";
    internal const string MissingOwner = "The owned native connection RF3 scenario did not initialize.";
    internal const string NativeMismatch = "The native connection witness does not match its signed RF3 operation.";
    internal const int OneConnection = 1;
    internal const int Absent = 0;
    internal const long InitialRevision = 1;
    internal const long UpdatedRevision = 2;
    internal const long InvalidExpectedRevision = 9;
    internal const long RevokedPolicyEpoch = 2;
}
