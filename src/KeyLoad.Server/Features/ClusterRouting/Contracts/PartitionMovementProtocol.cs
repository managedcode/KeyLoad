namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementProtocol
{
    internal const string ActionAlias = "keyload.server.v1.PartitionMovementTransportAction";
    internal const string ReplyAlias = "keyload.server.v1.PartitionMovementTransportReply";
    internal const string RequestAlias = "keyload.server.v1.PartitionMovementTransportRequest";
    internal const string Path = "/internal/partitions/movement/v1";
    internal const string ContentType = "application/octet-stream";
    internal const string SignatureHeader = "X-KeyLoad-Movement-Proof";
    internal const string InvalidProof = "The partition movement peer proof is invalid.";
    internal const string Unavailable = "The partition movement receiver is unavailable.";
}
