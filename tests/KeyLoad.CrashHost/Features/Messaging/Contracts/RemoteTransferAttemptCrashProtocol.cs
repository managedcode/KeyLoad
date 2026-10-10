namespace KeyLoad.CrashHost.Features.Messaging;

internal static class RemoteTransferAttemptCrashProtocol
{
    internal const string Mode = "remote-transfer-capacity-attempt";
    internal const string OperationFile = "transfer-advance-operation.json";
    internal const string FailureFile = "transfer-original-failure.bin";
    internal const string AcceptFile = "transfer-original-accept.json";
    internal const string SourceKey = "source";
    internal const string DestinationKey = "target";
    internal const string SourceQueue = "outgoing";
    internal const string DestinationQueue = "incoming";
    internal const string OriginalMessage = "transfer-original";
    internal const string FillerMessage = "transfer-filler";
    internal const string Payload = "{\"transfer\":true}";
    internal const string Missing = "The actual transfer attempt process state is missing.";
    internal const int InitialAttempts = 0;
    internal const string EmptyToken = "";
    internal const string EmptyHeaders = "{}";
    internal const int Ceiling = 2;
    internal const long OriginalGeneration = 1;
    internal const long AdvancedGeneration = 2;
    internal const long SingleStoredMessage = 1;
    internal const long PositionStep = 1;
}
