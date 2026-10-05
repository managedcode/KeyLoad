namespace KeyLoad.Server.Features.ClusterRouting;

internal static class RequestCqrsProbeProtocol
{
    internal const string ConfigurationSection = "KeyLoad:RequestCqrsProbe";
    internal const string EnabledSetting = "Enabled";
    internal const string RootSetting = "Root";
    internal const string SessionSetting = "SessionId";
    internal const string DiscoveryCaptureSetting = "DiscoveryCaptureMode";
    internal const string DiscoveryCaptureDisabled = "disabled";
    internal const string MixedInterface3Capture = "mixed-interface3-v1";
    internal const string DiscoveryKind = "DiscoveryObservation";
    internal const string DiscoveryFilePrefix = "discovery-";
    internal const string DiscoveryFileZero = "discovery-00.json";
    internal const string DiscoveryFileOne = "discovery-01.json";
    internal const int MaximumDiscoveryRecords = 2;
    internal const string FixedRoot = "/request-probes";
    internal const string OwnerFile = "owner.json";
    internal const string OwnerKind = "Owner";
    internal const string ArmKind = "Arm";
    internal const string ReleaseKind = "Release";
    internal const string MarkerKind = "Marker";
    internal const string TemporaryFilePrefix = "tmp-";
    internal const string ArmFilePrefix = "arm-";
    internal const string ReleaseFilePrefix = "release-";
    internal const string MarkerFilePrefix = "marker-";
    internal const string JsonFileSuffix = ".json";
    internal const string SessionIdFormat = "N";
    internal const string PrincipalPrefix = "c1-probe-";
    internal const string InvalidOptions = "The private request probe configuration is invalid.";
    internal const string InvalidRecord = "The private request probe record is invalid.";
    internal const string InvalidFiles = "The private request probe files are invalid.";
    internal const string OrdinaryMessage = "keyload-c1-private-probe-ordinary-canary";
    internal const string OrdinaryDataKey = "keyload-c1-private-probe-data";
    internal const string OrdinaryDataValue = "keyload-c1-private-probe-data-canary";
    internal const int Version = 1;
    internal const int MaximumJsonDepth = 4;
    internal const int MaximumPrincipalBytes = 256;
    internal const int MaximumRecordBytes = 8_192;
    internal const int MaximumFiles = 400;
    internal const int MaximumArms = 32;
    internal const int MaximumMarkersPerRequest = 8;
    internal const int MaximumAggregateBytes = 1_048_576;
    internal const int ReadBufferBytes = 8_193;
    internal const UnixFileMode PrivateDirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    internal const UnixFileMode PrivateFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
}
