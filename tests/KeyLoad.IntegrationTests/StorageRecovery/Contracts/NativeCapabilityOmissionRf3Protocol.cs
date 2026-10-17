namespace KeyLoad.IntegrationTests.Features.StorageRecovery;

internal static class NativeCapabilityOmissionRf3Protocol
{
    internal const string DocumentId = "current-native-id7-refused-then-repaired";
    internal const string DocumentJson = "{\"nativeCapability\":\"current-only\",\"complete\":true}";
    internal const string PutKind = "putDocument";
    internal const long Revision = 1;
    internal const string MissingState = "The genuine signed omitted-capability RF3 owner is absent.";
    internal const string EnabledArgument = "--KeyLoadTests:NativeDiscoveryOmission:Enabled=true";
    internal const string RootArgument = "--KeyLoadTests:NativeDiscoveryOmission:Root=";
    internal const string SessionArgument = "--KeyLoadTests:NativeDiscoveryOmission:SessionId=";
}
