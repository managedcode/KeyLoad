using KeyLoad.Query;

namespace KeyLoad.Orleans;

internal interface IRemoteAnnReadRouter
{
    Task<object> ReadAnnAsync(GrainRequestEnvelope original, PrincipalRecord principal,
        GrainReadKind kind, ReadOnlyMemory<byte> payload, SearchEngine actualSearch,
        CancellationToken cancellationToken);
}
