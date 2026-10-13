using KeyLoad.Orleans;

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
        var signed = global::KeyLoad.Server.Features.GraphTraversal.RemoteGraphGatewaySigning.Handles(readKind, commandKind)
            ? global::KeyLoad.Server.Features.GraphTraversal.RemoteGraphGatewaySigning.Create(codec,
                services.GetRequiredService<TimeProvider>(), requestId, principal.Id,
                readKind, commandKind, commandId, payload)
            : readKind is { } read ? codec.CreateRead(requestId, principal.Id, read, payload)
                : global::KeyLoad.Server.Features.EventStreams.EventVectorGatewaySigning.Create(
                    codec, requestId, principal.Id, commandKind!.Value, commandId, payload);
        var receivingOnly = global::KeyLoad.Server.Features.EventStreams.EventVectorGatewaySigning
            .IsReceivingOnly(commandKind, payload);
        return new(principal, requestId, signed, commandKind.HasValue && !receivingOnly, receivingOnly);
    }
}
