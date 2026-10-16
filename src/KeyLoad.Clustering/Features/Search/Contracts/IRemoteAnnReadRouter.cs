using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Query;

namespace KeyLoad.Clustering;

internal interface IRemoteAnnReadRouter
{
    Task<object> ReadAnnAsync(GrainRequestEnvelope original, PrincipalRecord principal,
        GrainReadKind kind, ReadOnlyMemory<byte> payload, SearchEngine actualSearch,
        CancellationToken cancellationToken, IResourceLifetimeWorkBinding binding);
}
