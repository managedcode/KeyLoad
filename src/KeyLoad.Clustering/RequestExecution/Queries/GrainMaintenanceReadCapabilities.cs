using KeyLoad.Clustering.Features.Search;
using KeyLoad.Core.Features.ResourceExecution;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.Clustering;

/// <summary>Runs the original administrator-only execution maintenance pair inside the caller request grain.</summary>
internal static class GrainMaintenanceReadCapabilities
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
            GrainReadKind.OnlineTextMaintenance => await services.GetRequiredService<Func<IOnlineTextMaintenance>>()().ExecuteBoundAsync(principal,
                GrainPayload.Read<OnlineTextCapabilityRequest>(request.Payload), request.Envelope.ExpiresAt,
                binding, cancellationToken).ConfigureAwait(true),
            GrainReadKind.TextMaintenance => await services.GetRequiredService<Func<ITextMaintenance>>()().ExecuteBoundAsync(principal,
                GrainPayload.Read<TextMaintenanceCapabilityRequest>(request.Payload), request.Envelope.ExpiresAt, binding, cancellationToken).ConfigureAwait(true),
            GrainReadKind.AnnMaintenance => await services.GetRequiredService<Func<IAnnMaintenance>>()().ExecuteBoundAsync(principal,
                GrainPayload.Read<AnnMaintenanceCapabilityRequest>(request.Payload), request.Envelope.ExpiresAt, binding, cancellationToken).ConfigureAwait(true),
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, GrainRoutingProtocol.InvalidRequest)
        };
    }
}
