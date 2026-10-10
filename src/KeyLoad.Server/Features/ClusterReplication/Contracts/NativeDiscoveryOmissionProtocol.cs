namespace KeyLoad.Server;

internal static class NativeDiscoveryOmissionProtocol
{
    internal const string Section = "KeyLoad:NativeDiscoveryOmission";
    internal const string FixedRoot = "/native-discovery-omission";
    internal const string Enabled = "Enabled";
    internal const string Root = "Root";
    internal const string SessionId = "SessionId";
    internal const string ArmFile = "arm.json";
    internal const string RequestFile = "request.json";
    internal const string OmittedFile = "omitted.json";
    internal const string VerifiedFile = "verified.json";
    internal const string LockFile = "publication.lock";
    internal const string SdkRoute = "sdk-command";
    internal const string McpRoute = "official-mcp";
    internal const string SourceStage = "source-authenticate";
    internal const string OmittedStage = "producer-id7-omitted";
    internal const string VerifiedStage = "consumer-id7-absent";
    internal const string Invalid = "NativeDiscoveryOmissionInvalid";
    internal const int Version = 1;
    internal const int SettingsCount = 3;
    internal const int ExcessEntry = 1;
    internal const int EmptyCount = 0;
    internal const int JsonDepth = 4;
}
