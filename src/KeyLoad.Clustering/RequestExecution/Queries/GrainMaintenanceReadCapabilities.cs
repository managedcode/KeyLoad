using KeyLoad.Clustering.Search;
using KeyLoad.Core.ResourceExecution;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.Clustering;

/// <summary>Runs the original administrator-only execution maintenance pair inside the caller request grain.</summary>
internal static class GrainMaintenanceReadCapabilities
{
    internal static bool Handles(GrainReadKind kind)
        => kind is GrainReadKind.TextMaintenance or GrainReadKind.AnnMaintenance or GrainReadKind.OnlineTextMaintenance;

    internal static async Task<GrainValue> ExecuteAsync(IServiceProvider services, PrincipalRecord principal,
        GrainReadKind kind, DecodedGrainRequest request, IResourceLifetimeWorkBinding binding, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(binding) /* KLD0041: BCL null validation accepts the binding IResourceLifetimeWorkBinding reference through its object parameter. */;
        GrainRequestAuthority.RequireAdministrator(principal);
        return kind switch
        {
            GrainReadKind.OnlineTextMaintenance => new GrainValue(await services.GetRequiredService<Func<IOnlineTextMaintenance>>()().ExecuteBoundAsync(principal,
                (GrainPayload.Read<OnlineTextCapabilityRequest>((request.Payload /* KLD0041: Read this verified native envelope Payload slot only for the selected closed-kind handler to validate its concrete request type. */)) /* KLD0041: Validate the received generated envelope payload as KeyLoad.Clustering.Search.OnlineTextCapabilityRequest at this closed operation boundary before executing typed code. */),
                binding, cancellationToken)) /* KLD0041: The current GrainValue wire slot carries this AnnMaintenance response with its generated runtime type header. */,
            GrainReadKind.TextMaintenance => new GrainValue(await services.GetRequiredService<Func<ITextMaintenance>>()().ExecuteBoundAsync(principal,
                (GrainPayload.Read<TextMaintenanceCapabilityRequest>((request.Payload /* KLD0041: Read this verified native envelope Payload slot only for the selected closed-kind handler to validate its concrete request type. */)) /* KLD0041: Validate the received generated envelope payload as KeyLoad.Clustering.TextMaintenanceCapabilityRequest at this closed operation boundary before executing typed code. */), binding, cancellationToken)) /* KLD0041: The current GrainValue wire slot carries this TextMaintenance response with its generated runtime type header. */,
            GrainReadKind.AnnMaintenance => new GrainValue(await services.GetRequiredService<Func<IAnnMaintenance>>()().ExecuteBoundAsync(principal,
                (GrainPayload.Read<AnnMaintenanceCapabilityRequest>((request.Payload /* KLD0041: Read this verified native envelope Payload slot only for the selected closed-kind handler to validate its concrete request type. */)) /* KLD0041: Validate the received generated envelope payload as KeyLoad.Clustering.AnnMaintenanceCapabilityRequest at this closed operation boundary before executing typed code. */), binding, cancellationToken)) /* KLD0041: The current GrainValue wire slot carries this AnnMaintenance response with its generated runtime type header. */,
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, GrainRoutingProtocol.InvalidRequest)
        };
    }
}
