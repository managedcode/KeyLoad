using KeyLoad.Clustering;

namespace KeyLoad.Server.Features.ResourceExecution;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(NativeResourceRetirementDiagnosticProtocol.WitnessAlias)]
internal sealed record NativeResourceRetirementDiagnosticWitness(
    [property: global::Orleans.Id(0)] NativeResourceRetirementDiagnosticRequest Request,
    [property: global::Orleans.Id(1)] ReplicaSiloDiscovery Owner,
    [property: global::Orleans.Id(2)] ResourceRetirementStateV1? State,
    [property: global::Orleans.Id(3)] long ReadBytes,
    [property: global::Orleans.Id(4)] int ExaminedRecords)
{
    [global::Orleans.Id(5)]
    public ResourceRetirementPreparationStateV1? Preparation
    {
        get; init;
    }
}
