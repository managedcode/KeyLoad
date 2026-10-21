using KeyLoad.Clustering;

namespace KeyLoad.Server;

internal static class CanonicalOperationRequestPreparation
{
    internal readonly record struct Prepared(PrincipalRecord Principal, Guid RequestId,
        string SignedRequest, bool Command);

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
        var actual = CanonicalOperationSigning.Create(context.RequestServices, principal,
            readKind, commandKind, commandId, payload, cancellationToken);
        // Native file transfers compose several signed metadata operations in one HTTP response.
        if (readKind != GrainReadKind.FileMetadata && commandKind != OperationKind.FileMetadata)
        {
            OperationResponseHeaders.Publish(context, actual.RequestId);
        }
        RequestFailureDiagnostic.MarkOperationDispatch(context, actual.Command);
        return actual;
    }
}
