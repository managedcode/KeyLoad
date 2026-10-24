namespace KeyLoad.Core.Messaging;

internal static class RemoteTransferAcceptFailureReferenceFields
{
    internal const uint Generation = 0;
    internal const uint AcceptCommandId = 1;
    internal const uint OutcomeDigest = 2;
    internal const uint WitnessToken = 3;
    internal const string Alias = "keyload.core.queue-transfer.accept-failure-reference";
}
