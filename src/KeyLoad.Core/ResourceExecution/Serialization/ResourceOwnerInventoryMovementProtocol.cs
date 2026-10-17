namespace KeyLoad.Core.Features.ResourceExecution;

internal static class ResourceOwnerInventoryMovementProtocol
{
    internal const int Version = 1;
    internal const int First = 0;
    internal const int Step = 1;
    internal const int ActiveImportProbeCount = 1;
    internal const string ProofAlias = "keyload.resource-owner-inventory.movement-proof.v1";
    internal const string ImportAlias = "keyload.resource-owner-inventory.import-origin.v1";
    internal const string ImportSpace = "resource-owner-inventory-import-v1";
    internal const string Invalid = "The resource inventory movement proof is invalid.";
    internal const string Capacity = "The resource inventory movement metadata exceeds the original bounds.";
}
