namespace KeyLoad.Server.Features.DocumentStorage;

internal static class RemoteResourceLifetimeReadProtocol
{
    internal const int Version = 2;
    internal const string KindAlias = "keyload.server.resource-lifetime-read.kind.v2";
    internal const string CallAlias = "keyload.server.resource-lifetime-read.call.v2";
    internal const string ReplyAlias = "keyload.server.resource-lifetime-read.reply.v2";
    internal const string InvalidProof = "The resource lifetime read proof is invalid.";
    internal const string Unavailable = "The resource lifetime read is unavailable.";
    internal const int MinimumBytes = 1;
    internal const int FirstVoter = 0;
}
