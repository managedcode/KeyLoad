
namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class RemoteTransferColdProtocol
{
    internal const string CreateKind = "createQueueTransfer";
    internal const string AcceptKind = "acceptQueueTransfer";
    internal const string CompleteKind = "completeQueueTransfer";
    internal const string TransferIdFormat = "N";
    internal const string Original = "cold-transfer-original";
    internal const string Healthy = "cold-transfer-healthy";
    internal const string InspectSource = "keyload_queue_transfer_inspect";
    internal const string InspectReceipt = "keyload_queue_transfer_receipt";
    internal const string ReadGrant = "messaging.secret.read";
    internal const string UseGrant = "messaging.secret.use";
    internal const string ChangedPayload = "{\"secret\":\"changed-transfer\"}";
    internal const string HealthyPayload = "{\"secret\":\"healthy-transfer\"}";
    internal const string Missing = "The original transfer state was not retained.";
    internal const long OriginalReadySequence = 1;
    internal const long HealthyReadySequence = 2;
    internal const int InitialAttempts = 0;
    internal const long CreateRevision = 1;
    internal const long CompleteRevision = 2;
    internal const long InitialStateVersion = 1;
    internal const int NoFailures = 0;
    internal const int FirstTokenCharacter = 0;
    internal const int TokenSuffixOffset = 1;
    internal const char FirstTokenReplacement = 'A';
    internal const char SecondTokenReplacement = 'B';
    internal const Capability Source = Capability.QueuePublish | Capability.QueueInspect | Capability.Query;
    internal const Capability Target = Source | Capability.QueueConsume | Capability.QueueAck;
}
