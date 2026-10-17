using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

/// <summary>Selects the configured borrowed Blob router after native fresh-principal admission.</summary>
internal sealed class GrainBlobReadExecution(DatabaseEngine database, IServiceProvider services, TimeProvider clock)
{
    private readonly GrainBlobReadCapabilities local = new(database);

    internal async Task<object?> ExecuteAsync(GrainReadKind kind, PrincipalRecord principal,
        DecodedGrainRequest request, IResourceLifetimeWorkBinding binding, CancellationToken cancellationToken)
    {
        if (services.GetService<IRemoteBlobReadRouter>() is { } remote)
        {
            return await remote.ReadAsync(request.Envelope, principal, request.Payload, binding, cancellationToken)
                .ConfigureAwait(true);
        }
        var work = new ReadExecutionBudget(services.GetRequiredService<IOptions<DatabaseLimits>>(), clock, cancellationToken);
        return local.Execute(kind, principal.Id, request.Payload, work, binding);
    }
}
