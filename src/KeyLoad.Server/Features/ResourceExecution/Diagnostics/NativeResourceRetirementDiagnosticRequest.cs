namespace KeyLoad.Server.Features.ResourceExecution;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(NativeResourceRetirementDiagnosticProtocol.RequestAlias)]
internal sealed record NativeResourceRetirementDiagnosticRequest(
    [property: global::Orleans.Id(0)] ResourceRetirementRequestV1 Request,
    [property: global::Orleans.Id(1)] int PageSize,
    [property: global::Orleans.Id(2)] Guid Nonce)
{
    [global::Orleans.Id(3)]
    public ResourceRetirementDiagnosticOperation Operation
    {
        get; init;
    }
}
