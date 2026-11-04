using KeyLoad.Core;

namespace KeyLoad.Clustering;

/// <summary>Routes bounded node observations after a canonical read cut and current persisted authorization.</summary>
internal static class GrainAdminDashboardCapabilities
{
    internal static bool Handles(GrainReadKind kind) => kind is GrainReadKind.AdminDashboard
        or GrainReadKind.AdminResources or GrainReadKind.AdminQueue;

    internal static async Task<GrainValue> ExecuteAsync(DatabaseEngine database, ISiloDatabaseService administration,
        PrincipalRecord principal, GrainReadKind kind, DecodedGrainRequest request, GrainRequestCodec codec,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        GrainRequestAuthority.RequireAdministrator(principal);
        cancellationToken.ThrowIfCancellationRequested();
        if (kind == GrainReadKind.AdminDashboard)
        {
            GrainPayload.RequireNoDto((request.Payload /* KLD0041: Read this verified native envelope Payload slot only for the selected closed-kind handler to validate its concrete request type. */)) /* KLD0041: Validate the current generated envelope scalar NoDto marker for this capability before executing a request without a DTO. */;
            var result = await administration.ReadDashboardAsync(cancellationToken);
            ConnectionReadExecution.ValidateFreshRequest(codec, request, request.Envelope.RequestId, cancellationToken);
            GrainRequestAuthority.RequireAdministrator(GrainRequestAuthority.ReloadForRequest(database,
                request.Envelope, clock));
            return new GrainValue(result) /* KLD0041: The current GrainValue wire slot carries this AdminResources response with its generated runtime type header. */;
        }
        return kind switch
        {
            GrainReadKind.AdminResources => new GrainValue(new AdminCatalogReader(database).Read(principal.Id,
                (GrainPayload.Read<AdminResourcesRequest>((request.Payload /* KLD0041: Read this verified native envelope Payload slot only for the selected closed-kind handler to validate its concrete request type. */)) /* KLD0041: Validate the received generated envelope payload as KeyLoad.AdminResourcesRequest at this closed operation boundary before executing typed code. */), cancellationToken)) /* KLD0041: The current GrainValue wire slot carries this AdminResources response with its generated runtime type header. */,
            GrainReadKind.AdminQueue => new GrainValue(new AdminQueueReader(database).Read(principal.Id,
                (GrainPayload.Read<AdminQueueRequest>((request.Payload /* KLD0041: Read this verified native envelope Payload slot only for the selected closed-kind handler to validate its concrete request type. */)) /* KLD0041: Validate the received generated envelope payload as KeyLoad.AdminQueueRequest at this closed operation boundary before executing typed code. */), cancellationToken)) /* KLD0041: The current GrainValue wire slot carries this AdminQueue response with its generated runtime type header. */,
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, GrainRoutingProtocol.InvalidRequest)
        };
    }
}
