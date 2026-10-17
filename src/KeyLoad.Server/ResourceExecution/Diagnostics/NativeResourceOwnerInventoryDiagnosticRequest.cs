namespace KeyLoad.Server.Features.ResourceExecution;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(NativeResourceOwnerInventoryDiagnosticProtocol.RequestAlias)]
internal sealed record NativeResourceOwnerInventoryDiagnosticRequest(
    [property: global::Orleans.Id(0)] string TenantId,
    [property: global::Orleans.Id(1)] string DatabaseId,
    [property: global::Orleans.Id(2)] string ResourceName,
    [property: global::Orleans.Id(3)] int PageSize,
    [property: global::Orleans.Id(4)] Guid Nonce);
