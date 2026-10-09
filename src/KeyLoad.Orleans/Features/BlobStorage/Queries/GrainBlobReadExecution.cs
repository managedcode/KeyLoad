using KeyLoad.Core;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.Orleans;

/// <summary>Selects the configured borrowed Blob router after native fresh-principal admission.</summary>
internal sealed class GrainBlobReadExecution(DatabaseEngine database, IServiceProvider services)
{
    private readonly GrainBlobReadCapabilities local = new(database);

    internal async Task<object?> ExecuteAsync(GrainReadKind kind, PrincipalRecord principal,
        DecodedGrainRequest request, CancellationToken cancellationToken)
    {
        if (services.GetService<IRemoteBlobReadRouter>() is { } remote)
        {
            return await remote.ReadAsync(request.Envelope, principal, request.Payload, cancellationToken)
                .ConfigureAwait(true);
        }
        return local.Execute(kind, principal.Id, request.Payload, cancellationToken);
    }
}
