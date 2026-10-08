namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

internal enum FollowerDocumentCaller { Sdk, OfficialMcp, SqlSdk, SqlOfficialMcp }
internal enum FollowerDocumentChange { Document, Credential, Grant, FieldPolicy, Lag, Cancellation, NoQuorum }

internal static class FollowerDocumentRf3Protocol
{
    internal const string Tool = "keyload_documents_read_follower";
    internal const string RedactedOld = "{\"value\":1}";
    internal const string ProtectedPath = "/cohort";
    internal const string FieldGrant = "follower.cohort.read";
    internal const string PrivateMarker = "same-data-epoch";
    internal const string KeySuffix = "-key";
    internal const int Version = 1;
    internal const long FirstRevision = 1;
    internal const long SecondRevision = 2;
    internal const long ZeroLag = 0;
    internal const long UnlimitedLag = long.MaxValue;
    internal const string MissingOwner = "The native follower flow did not acquire its owned resource.";
}
