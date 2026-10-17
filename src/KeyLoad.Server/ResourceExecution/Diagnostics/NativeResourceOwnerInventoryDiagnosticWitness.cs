using KeyLoad.Clustering;

namespace KeyLoad.Server.Features.ResourceExecution;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(NativeResourceOwnerInventoryDiagnosticProtocol.WitnessAlias)]
internal sealed record NativeResourceOwnerInventoryDiagnosticWitness(
    [property: global::Orleans.Id(0)] NativeResourceOwnerInventoryDiagnosticRequest Request,
    [property: global::Orleans.Id(1)] ReplicaSiloDiscovery Owner,
    [property: global::Orleans.Id(2)] ResourceOwnerInventoryResultV1 Inventory,
    [property: global::Orleans.Id(3)] long ReadBytes,
    [property: global::Orleans.Id(4)] int ExaminedRecords);
