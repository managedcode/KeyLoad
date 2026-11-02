using KeyLoad.Clustering;

namespace KeyLoad.Server;

internal static class CanonicalOperationIssuance
{
    internal static CanonicalOperationRequestPreparation.Prepared Create<TPayload>(IServiceProvider services,
        PrincipalRecord principal, GrainReadKind? readKind, OperationKind? commandKind,
        Guid commandId, TPayload payload, CancellationToken token) where TPayload : notnull
    {
        token.ThrowIfCancellationRequested();
        var requestId = Guid.NewGuid();
        var codec = services.GetRequiredService<GrainRequestCodec>();
        var envelope = readKind is { } read
            ? codec.CreateRead(requestId, principal.Id, read, payload)
            : codec.CreateCommand(requestId, principal.Id, commandKind!.Value, commandId, payload);
        return new(principal, requestId, envelope, commandKind.HasValue);
    }
}
