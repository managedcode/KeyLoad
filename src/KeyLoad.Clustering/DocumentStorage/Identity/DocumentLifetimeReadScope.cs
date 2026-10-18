namespace KeyLoad.Clustering;

internal static class DocumentLifetimeReadScope
{
    internal const string OwnedResultAlias = "keyload.orleans.v2.OwnedDocumentReadResult";
    internal const string Purpose = "keyload-document-lifetime-read-v2";

    internal static bool Validate(GrainRequestEnvelope request)
    {
        var document = request.ReadKind is GrainReadKind.OwnedDocumentV2;
        if (request.Purpose != Purpose)
        {
            if (document)
            {
                throw Errors.Fail(ErrorCode.PermissionDenied, GrainRoutingProtocol.AdministrationRequired);
            }
            return false;
        }
        if (!document || request.CommandKind is not null)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        return true;
    }
}
