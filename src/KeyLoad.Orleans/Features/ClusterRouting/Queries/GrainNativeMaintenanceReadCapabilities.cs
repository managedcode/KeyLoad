using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.Orleans;

/// <summary>Runs the original administrator-only native maintenance pair inside the caller request grain.</summary>
internal static class GrainNativeMaintenanceReadCapabilities
{
    internal static bool Handles(GrainReadKind kind)
        => kind is GrainReadKind.TextMaintenance or GrainReadKind.AnnMaintenance;

    internal static async Task<object?> ExecuteAsync(IServiceProvider services, PrincipalRecord principal,
        GrainReadKind kind, DecodedGrainRequest request, CancellationToken cancellationToken)
    {
        GrainRequestAuthority.RequireAdministrator(principal);
        return kind switch
        {
            GrainReadKind.TextMaintenance => await services.GetRequiredService<INativeTextMaintenance>().ExecuteAsync(principal,
                GrainNativePayload.Read<TextMaintenanceCapabilityRequest>(request.Payload), cancellationToken).ConfigureAwait(true),
            GrainReadKind.AnnMaintenance => await services.GetRequiredService<INativeAnnMaintenance>().ExecuteAsync(principal,
                GrainNativePayload.Read<AnnMaintenanceCapabilityRequest>(request.Payload), cancellationToken).ConfigureAwait(true),
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, GrainRoutingProtocol.InvalidRequest)
        };
    }
}
