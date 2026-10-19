using KeyLoad.Clustering;

namespace KeyLoad.Server;

internal static class CanonicalOperationSigning
{
    internal static CanonicalOperationRequestPreparation.Prepared Create(IServiceProvider services,
        PrincipalRecord principal, GrainReadKind? readKind, OperationKind? commandKind,
        Guid commandId, ReadOnlyMemory<byte> payload, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var requestId = Guid.NewGuid();
        var codec = services.GetRequiredService<GrainRequestCodec>();
        var signed = readKind is { } read
            ? codec.CreateRead(requestId, principal.Id, read, payload)
            : codec.CreateCommand(requestId, principal.Id, commandKind!.Value, commandId, payload);
        return new(principal, requestId, signed, commandKind.HasValue);
    }
}
