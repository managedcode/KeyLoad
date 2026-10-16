using KeyLoad.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

internal static class ResourceLifecycleReadExecution
{
    internal static bool Handles(GrainReadKind kind)
        => kind is GrainReadKind.ResourceOwnerInventory or GrainReadKind.ResourceRetirement;

    internal static async Task<object?> ExecuteAsync(DatabaseEngine database, IServiceProvider services,
        TimeProvider clock, PrincipalRecord principal, DecodedGrainRequest request, GrainRequestCodec codec,
        IGrainContext context, ReadExecutionBudget? retirementWork, CancellationToken token)
    {
        if (request.Envelope.ReadKind == GrainReadKind.ResourceOwnerInventory)
        {
            return await ResourceOwnerInventoryObservedReadExecution.ExecuteAsync(database, services, clock,
                principal, request, codec, context, token).ConfigureAwait(true);
        }
        return ResourceRetirementReadExecution.Execute(database, principal, request,
            services.GetRequiredService<ReplicaSiloDiscoveryState>(),
            services.GetRequiredService<IOptions<DatabaseLimits>>(), clock,
            retirementWork ?? throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementReadScope.InvalidRequest), token);
    }
}
