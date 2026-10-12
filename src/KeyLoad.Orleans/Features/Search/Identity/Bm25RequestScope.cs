namespace KeyLoad.Orleans;

internal static class Bm25RequestScope
{
    internal const string Purpose = "keyload-bm25-corpus-publication-verified-v1";

    internal static bool Validate(GrainRequestEnvelope request)
    {
        var privateKind = request.CommandKind == OperationKind.Bm25CorpusPublicationPhase;
        if (request.Purpose != Purpose && !privateKind)
        { return false; }
        if (request.Purpose != Purpose || !privateKind || request.ReadKind is not null)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest); }
        return true;
    }
}
