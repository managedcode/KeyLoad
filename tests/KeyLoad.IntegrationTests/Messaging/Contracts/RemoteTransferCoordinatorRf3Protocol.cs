namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class RemoteTransferCoordinatorRf3Protocol
{
    internal const string SubjectPrefix = "transfer-coordinator-";
    internal const string SensitivePolicy = "messaging-private";
    internal const string Filler = "coordinator-quota-filler";
    internal const string FillerPayload = "{\"fill\":true}";
    internal const string Missing = "The actual transfer coordinator state was not retained.";
    internal const string Scenario = "queue-transfer-coordinator-cold";
    internal const int SingleStoredMessage = 1;
    internal const long OriginalReady = 1;
    internal const long RepairedReady = 2;
    internal const long CompleteRevision = 2;
}
