namespace KeyLoad.Server.Features.EventStreams;

internal static class EventVectorRemoteProtocol
{
    internal const int Version = 1;
    internal const int NonceBytes = 16;
    internal const string Path = "/internal/event-vector/source";
    internal const string Purpose = "keyload-event-vector-source-phase-v1";
    internal const string CallAlias = "keyload.server.event-vector-call.v1";
    internal const string ReplyAlias = "keyload.server.event-vector-reply.v1";
    internal const string Invalid = "The native event vector source proof is invalid.";
    internal const string Unavailable = "The native event vector source operation is unavailable.";
}
