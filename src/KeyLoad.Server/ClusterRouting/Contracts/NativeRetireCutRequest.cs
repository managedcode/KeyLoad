namespace KeyLoad.Server.Features.ClusterRouting;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(NativeRetireCutWire.RequestAlias)]
internal sealed record NativeRetireCutRequest(
    [property: global::Orleans.Id(0)] Guid Nonce,
    [property: global::Orleans.Id(1)] Guid ArmId,
    [property: global::Orleans.Id(2)] string SessionId,
    [property: global::Orleans.Id(3)] Guid CommandId,
    [property: global::Orleans.Id(4)] string PrincipalId,
    [property: global::Orleans.Id(5)] CommitToken? ReceiptToken = null);
