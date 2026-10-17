namespace KeyLoad.Server.Features.ResourceExecution;

/// <summary>Binds exact typed Core result bytes or a flat safe error to the complete original request MAC.</summary>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(ResourceOwnerInventoryNativeProtocol.ReplyAlias)]
internal sealed record ResourceOwnerInventoryNativeReply(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] Guid RequestId,
    [property: global::Orleans.Id(2)] Guid Nonce,
    [property: global::Orleans.Id(3)] string RequestSignature,
    [property: global::Orleans.Id(4)] ReadOnlyMemory<byte> Payload,
    [property: global::Orleans.Id(5)] ErrorCode? Error,
    [property: global::Orleans.Id(6)] string? SafeDetail,
    [property: global::Orleans.Id(7)] string Signature);
