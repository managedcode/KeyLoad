using KeyLoad.Clustering;

namespace KeyLoad.Server;

internal static class CanonicalOperationRequestPreparation
{
    internal readonly record struct Prepared(PrincipalRecord Principal, Guid RequestId,
        GrainRequestEnvelope Envelope, bool Command);

    internal static Prepared Create<TPayload>(HttpContext context, GrainReadKind? readKind,
        OperationKind? commandKind, Guid commandId, TPayload payload,
        CancellationToken cancellationToken) where TPayload : notnull
    {
        ArgumentNullException.ThrowIfNull(context) /* KLD0041: BCL null validation accepts the context HttpContext reference through its object parameter. */;
        cancellationToken.ThrowIfCancellationRequested();
        if ((((context.Items /* KLD0041: Read the verified principal from ASP.NET's native request-local Items dictionary before canonical operation issuance. */)[ServerProtocol.PrincipalItem] /* KLD0041: Read the verified principal from ASP.NET's native request-local Items dictionary before canonical operation issuance. */) is not PrincipalRecord principal /* KLD0042: Require the authentication-owned PrincipalRecord before issuing any operation or trusted request identity. */))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, ServerProtocol.MissingCredential);
        }
        var actual = CanonicalOperationIssuance.Create(context.RequestServices, principal,
            readKind, commandKind, commandId, payload, cancellationToken);
        // Native file transfers compose several metadata operations in one HTTP response.
        if (readKind != GrainReadKind.FileMetadata && commandKind != OperationKind.FileMetadata)
        {
            OperationResponseHeaders.Publish(context, actual.RequestId);
        }
        RequestFailureDiagnostic.MarkOperationDispatch(context, actual.Command);
        return actual;
    }
}
