using KeyLoad.Clustering.Features.Search;
using KeyLoad.Core.Features.ResourceExecution;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.Clustering;

/// <summary>Runs the original administrator-only native maintenance pair inside the caller request grain.</summary>
internal static class GrainNativeMaintenanceReadCapabilities
{
    internal static bool Handles(GrainReadKind kind)
        => kind is GrainReadKind.TextMaintenance or GrainReadKind.AnnMaintenance or GrainReadKind.OnlineTextMaintenance;

    internal static async Task<object?> ExecuteAsync(IServiceProvider services, PrincipalRecord principal,
        GrainReadKind kind, DecodedGrainRequest request, IResourceLifetimeWorkBinding binding, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(binding);
        GrainRequestAuthority.RequireAdministrator(principal);
        return kind switch
        {
            GrainReadKind.OnlineTextMaintenance => await services.GetRequiredService<Func<INativeOnlineTextMaintenance>>()().ExecuteBoundAsync(principal,
                GrainNativePayload.Read<OnlineTextCapabilityRequest>(request.Payload), request.Envelope.ExpiresAt,
                binding, cancellationToken).ConfigureAwait(true),
            GrainReadKind.TextMaintenance => await services.GetRequiredService<Func<INativeTextMaintenance>>()().ExecuteBoundAsync(principal,
                GrainNativePayload.Read<TextMaintenanceCapabilityRequest>(request.Payload), request.Envelope.ExpiresAt, binding, cancellationToken).ConfigureAwait(true),
            GrainReadKind.AnnMaintenance => await services.GetRequiredService<Func<INativeAnnMaintenance>>()().ExecuteBoundAsync(principal,
                GrainNativePayload.Read<AnnMaintenanceCapabilityRequest>(request.Payload), request.Envelope.ExpiresAt, binding, cancellationToken).ConfigureAwait(true),
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, GrainRoutingProtocol.InvalidRequest)
        };
    }
}
