namespace KeyLoad.Server.Features.DocumentStorage;

internal static class RemoteModelNativeProtocol
{
    internal const int InterfaceVersion = 1;
    internal const string ServiceAlias = "keyload.internal.remote-model.native.service.v1";
    internal const string ExchangeAlias = "keyload.internal.remote-model.native.exchange.v1";
    internal const string ChannelAlias = "keyload.internal.remote-model.native.channel.v1";
    internal const string RequestAlias = "keyload.internal.remote-model.native.request.v1";
    internal const string ReplyAlias = "keyload.internal.remote-model.native.reply.v1";
    internal const string MacFrameAlias = "keyload.internal.remote-model.native.mac-frame.v1";
    internal const string NativeRequestPurpose = "KeyLoad.RemoteModel.Native.Request.v1";
    internal const string NativeReplyPurpose = "KeyLoad.RemoteModel.Native.Reply.v1";
    internal const int MaximumFrameBytes = 512;
    internal const int MaximumEnvelopeBytes = RemoteDocumentProtocol.MaximumBodyBytes
        + global::KeyLoad.Orleans.ReplicaTransportProtocol.MaximumMetadataBytes;
    internal const string InvalidProof = "The native remote model proof is invalid.";
    internal const string Unavailable = "The configured model owner is unavailable.";
}
