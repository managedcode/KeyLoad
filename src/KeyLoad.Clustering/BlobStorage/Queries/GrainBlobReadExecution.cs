using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

/// <summary>Executes one Blob read against the local owner after native fresh-principal admission.</summary>
internal sealed class GrainBlobReadExecution(DatabaseEngine database, IServiceProvider services, TimeProvider clock)
{
    private readonly GrainBlobReadCapabilities local = new(database);

    internal object? Execute(GrainReadKind kind, PrincipalRecord principal,
        DecodedGrainRequest request, IResourceLifetimeWorkBinding binding, CancellationToken cancellationToken)
    {
        var work = new ReadExecutionBudget(services.GetRequiredService<IOptions<DatabaseLimits>>(), clock, cancellationToken);
        return local.Execute(kind, principal.Id, request.Payload, work, binding);
    }
}
