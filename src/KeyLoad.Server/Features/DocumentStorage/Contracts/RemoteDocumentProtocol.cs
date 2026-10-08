namespace KeyLoad.Server.Features.DocumentStorage;

internal static class RemoteDocumentProtocol
{
    internal const string CallAlias = "keyload.internal.remote-document.call.v1";
    internal const string ReplyAlias = "keyload.internal.remote-document.reply.v1";
    internal const string ControlledCallAlias = "keyload.internal.controlled-document.call.v1";
    internal const string EnvelopeAlias = "keyload.internal.remote-document.transport.v1";
    internal const int Version = 1;
    internal const int MaximumAdmissions = 8;
    internal const int MaximumBodyBytes = 8 * 1024 * 1024;
    internal const int SecretBytes = 32;
    internal const int SignatureCharacters = 64;
    internal const string Path = "/internal/documents/owned-read/v1";
    internal const string ContentType = "application/octet-stream";
    internal const string SignatureHeader = "X-KeyLoad-Document-Proof";
    internal const string RequestDomain = "KeyLoad.RemoteDocumentRead.Request.v1\0";
    internal const string ReplyDomain = "KeyLoad.RemoteDocumentRead.Reply.v1\0";
    internal const string InvalidProof = "The remote document read proof is invalid.";
    internal const string Unavailable = "The configured document owner is unavailable.";
}
