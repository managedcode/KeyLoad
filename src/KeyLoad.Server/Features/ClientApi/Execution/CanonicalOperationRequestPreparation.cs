using KeyLoad.Orleans;

namespace KeyLoad.Server;

internal static class CanonicalOperationRequestPreparation
{
    internal readonly record struct Prepared(PrincipalRecord Principal, Guid RequestId,
        string SignedRequest, bool Command, bool ReceivingOnly);

    internal static Prepared Create(HttpContext context, GrainReadKind? readKind,
        OperationKind? commandKind, Guid commandId, ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (context.Items[ServerProtocol.PrincipalItem] is not PrincipalRecord principal)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, ServerProtocol.MissingCredential);
        }
        if (readKind == GrainReadKind.FollowerDocument)
        { payload = FollowerReadCredentialCapability.Wrap(context, payload); }
        if (readKind == GrainReadKind.ClusterBackupOwner)
        { payload = ClusterBackupCredentialCapability.Wrap(context, payload); }
        var requestId = Guid.NewGuid();
        var codec = context.RequestServices.GetRequiredService<GrainRequestCodec>();
        var signed = readKind is { } read ? codec.CreateRead(requestId, principal.Id, read, payload)
            : global::KeyLoad.Server.Features.EventStreams.EventVectorGatewaySigning.Create(
                codec, requestId, principal.Id, commandKind!.Value, commandId, payload);
        var receivingOnly = global::KeyLoad.Server.Features.EventStreams.EventVectorGatewaySigning
            .IsReceivingOnly(commandKind, payload);
        var command = commandKind.HasValue && !receivingOnly;
        OperationResponseHeaders.Publish(context, requestId);
        RequestFailureDiagnostic.MarkOperationDispatch(context, command);
        return new Prepared(principal, requestId, signed, command, receivingOnly);
    }
}
