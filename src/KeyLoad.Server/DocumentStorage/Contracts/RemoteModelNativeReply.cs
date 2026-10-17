namespace KeyLoad.Server.Features.DocumentStorage;

/// <summary>The original inner result or a bounded authenticated native failure.</summary>
[GenerateSerializer, Alias(RemoteModelNativeProtocol.ReplyAlias)]
internal sealed record RemoteModelNativeReply(
    [property: Id(0)] ReadOnlyMemory<byte> Payload,
    [property: Id(1)] string Signature,
    [property: Id(2)] ErrorCode? Error,
    [property: Id(3)] string? SafeDetail,
    [property: Id(4)] Guid Nonce,
    [property: Id(5)] string SenderSignature);
