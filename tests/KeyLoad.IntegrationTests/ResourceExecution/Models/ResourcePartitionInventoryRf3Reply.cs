using KeyLoad.Server.Features.ResourceExecution;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal sealed record ResourcePartitionInventoryRf3Reply(
    NativeResourceOwnerInventoryDiagnosticWitness? Witness, ErrorCode? Error);
