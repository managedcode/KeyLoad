using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Query.Features.QueryExecution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.Orleans;

internal static class DistributedSearchLeafCapability
{
    internal static DistributedSearchLeafResultV1 Execute(DatabaseEngine database, IServiceProvider services,
        TimeProvider clock, PrincipalRecord principal, DecodedGrainRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var input = GrainNativePayload.Read<DistributedSearchOwnedLeafV1>(request.Payload);
        return DistributedSearchOwnedLeafExecution.Execute(database, principal.Id, input,
            services.GetRequiredService<PhysicalShardRecord>(),
            services.GetRequiredService<IOptions<QueryExecutionOptions>>(), clock,
            request.Envelope.ExpiresAt, token);
    }
}
