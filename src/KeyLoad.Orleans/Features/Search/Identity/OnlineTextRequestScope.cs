namespace KeyLoad.Orleans;

internal static class OnlineTextRequestScope
{
    internal const string Purpose = "keyload-online-text-capability-v1";
    internal const string CapabilityRequestAlias = "keyload.orleans.search.online-text-capability-request.v1";
    internal const string CapabilityResultAlias = "keyload.orleans.search.online-text-capability-result.v1";

    internal static bool Validate(GrainRequestEnvelope request)
    {
        var privateKind = request.ReadKind == GrainReadKind.OnlineTextMaintenance
            || request.CommandKind == OperationKind.OnlineTextPublicationPhase;
        if (request.Purpose != Purpose && !privateKind)
        { return false; }
        if (request.Purpose != Purpose || !privateKind
            || (request.ReadKind == GrainReadKind.OnlineTextMaintenance && request.CommandKind is not null)
            || (request.CommandKind == OperationKind.OnlineTextPublicationPhase && request.ReadKind is not null))
        { throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest); }
        return true;
    }
}
