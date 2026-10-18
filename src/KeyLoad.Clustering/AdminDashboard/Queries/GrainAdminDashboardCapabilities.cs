using KeyLoad.Core;

namespace KeyLoad.Clustering;

/// <summary>Routes bounded node observations after a canonical read cut and current persisted authorization.</summary>
internal static class GrainAdminDashboardCapabilities
{
    internal static bool Handles(GrainReadKind kind) => kind is GrainReadKind.AdminDashboard
        or GrainReadKind.AdminResources or GrainReadKind.AdminQueue;

    internal static async Task<object> ExecuteAsync(DatabaseEngine database, ISiloDatabaseService administration,
        PrincipalRecord principal, GrainReadKind kind, DecodedGrainRequest request, GrainRequestCodec codec,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        GrainRequestAuthority.RequireAdministrator(principal);
        cancellationToken.ThrowIfCancellationRequested();
        var payload = request.Payload;
        if (kind == GrainReadKind.AdminDashboard)
        {
            GrainNativePayload.RequireNoDto(payload);
            var result = await administration.ReadDashboardAsync(cancellationToken).ConfigureAwait(true);
            ConnectionReadExecution.ValidateFreshRequest(codec, request, request.Envelope.RequestId, cancellationToken);
            GrainRequestAuthority.RequireAdministrator(GrainRequestAuthority.ReloadForRequest(database,
                request.Envelope, clock));
            return result;
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
