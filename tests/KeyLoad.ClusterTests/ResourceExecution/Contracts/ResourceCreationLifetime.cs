using KeyLoad.Core.ResourceExecution;
using KeyLoad.Server;
using ManagedCode.Communication.Commands;

namespace KeyLoad.ClusterTests.ResourceExecution;

/// <summary>Derives the expected lifetime from the caller's original resource creation command.</summary>
internal static class ResourceCreationLifetime
{
    internal static EntityRefLifetime Create(Guid commandId, ConfigureResourceRequest request)
    {
        var original = Command<DatabaseCommand>.Create(nameof(DatabaseCommand),
            new DatabaseCommand(OperationKind.ConfigureResource, request), commandId);
        original.UserId = PartitionStoreProtocol.AdministratorId;
        var lifetime = ResourceLifetimeIdentity.Create(original, request.TenantId, request.DatabaseId, request.Definition);
        return new(lifetime.Incarnation.Value);
    }
}
