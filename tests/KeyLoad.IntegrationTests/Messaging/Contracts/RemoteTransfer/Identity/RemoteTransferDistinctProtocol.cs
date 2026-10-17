namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class RemoteTransferDistinctProtocol
{
    internal const string TechnicalSubject = "transfer-b-native-technical";
    internal const string LogicalPrefix = "transfer-a-logical-";
    internal const string CredentialPrefix = "transfer-native-key-";
    internal const string Separator = ".";
    internal const int SecretBytes = 32;
    internal const string Payload = "{\"origin\":\"a\",\"work\":1}";
    internal const string Headers = "{\"route\":\"distinct-b\"}";
    internal const string MessageId = "transfer-distinct-original";
    internal const string Missing = "The genuine distinct-owner transfer stage is absent.";
    internal const Capability SourceCapabilities = Capability.QueuePublish | Capability.QueueInspect;
    internal const Capability TargetCapabilities = Capability.QueuePublish | Capability.QueueInspect;
}
