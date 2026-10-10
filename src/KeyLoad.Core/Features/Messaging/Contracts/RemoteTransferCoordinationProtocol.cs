namespace KeyLoad.Core.Features.Messaging;

internal static class RemoteTransferCoordinationProtocol
{
    internal const string HintAlias = "keyload.core.queue-transfer-coordination-hint.v1";
    internal const string CommandDomain = "keyload.queue-transfer.coordinator-command.v1";
    internal const string AcceptStage = "accept";
    internal const string CompleteStage = "complete";
    internal const string Unavailable = "Queue transfer coordination has no configured principal.";
    internal const string InvalidHint = "The queue transfer coordination hint is inconsistent.";
    internal const string InvalidResult = "The queue transfer native result is inconsistent.";
    internal const int FirstIndex = 0;
    internal const int SingleRecord = 1;
    internal const int GuidBytes = 16;
}
