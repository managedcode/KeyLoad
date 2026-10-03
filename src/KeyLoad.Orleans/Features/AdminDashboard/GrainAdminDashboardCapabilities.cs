using KeyLoad.Core;

namespace KeyLoad.Orleans;

/// <summary>Routes bounded admin observations after the existing quorum read and authority reload.</summary>
internal static class GrainAdminDashboardCapabilities
{
    internal static bool Handles(GrainReadKind kind) => kind is GrainReadKind.AdminDashboard
        or GrainReadKind.AdminResources or GrainReadKind.AdminQueue;

    internal static async Task<object> ExecuteAsync(DatabaseEngine database, INodeAdministration administration,
        PrincipalRecord principal, GrainReadKind kind, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        GrainRequestAuthority.RequireAdministrator(principal);
        cancellationToken.ThrowIfCancellationRequested();
        if (kind == GrainReadKind.AdminDashboard)
        {
            GrainNativePayload.RequireNoDto(payload);
            return await administration.DashboardAsync(cancellationToken).ConfigureAwait(true);
        }
        return kind switch
        {
            GrainReadKind.AdminResources => new AdminCatalogReader(database).Read(principal.Id,
                GrainNativePayload.Read<AdminResourcesRequest>(payload), cancellationToken),
            GrainReadKind.AdminQueue => new AdminQueueReader(database).Read(principal.Id,
                GrainNativePayload.Read<AdminQueueRequest>(payload), cancellationToken),
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, GrainRoutingProtocol.InvalidRequest)
        };
    }
}
