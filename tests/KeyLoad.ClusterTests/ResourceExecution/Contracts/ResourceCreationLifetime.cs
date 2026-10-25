using System.Text.Json;
using KeyLoad.Core.InternalSerialization;
using KeyLoad.Core.ResourceExecution;
using KeyLoad.Server;
using ManagedCode.Communication.Commands;

namespace KeyLoad.ClusterTests.ResourceExecution;

/// <summary>Derives the expected lifetime from the caller's original resource creation command.</summary>
internal static class ResourceCreationLifetime
{
    internal static EntityRefLifetime Create(Guid commandId, ConfigureResourceRequest request)
    {
        var fingerprint = DatabaseCommandFingerprint.Compute(commandId, OperationKind.ConfigureResource,
            PartitionStoreProtocol.AdministratorId, JsonSerializer.Serialize(request, JsonDefaults.Options));
        var original = Command<DatabaseCommand>.Create(nameof(DatabaseCommand),
            new DatabaseCommand(OperationKind.ConfigureResource, OrleansSerialization.Serialize(request), fingerprint), commandId);
        original.UserId = PartitionStoreProtocol.AdministratorId;
        var lifetime = ResourceLifetimeIdentity.Create(original, request.TenantId, request.DatabaseId, request.Definition);
        return new(lifetime.Incarnation.Value);
    }
}
