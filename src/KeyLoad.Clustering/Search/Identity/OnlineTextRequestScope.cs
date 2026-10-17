namespace KeyLoad.Clustering;

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
        {
            return false;
        }
        if (!IsSelected(request))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        return true;
    }

    internal static bool IsSelected(GrainRequestEnvelope request)
        => request.Purpose == Purpose && (request.ReadKind is (GrainReadKind.OnlineTextMaintenance or GrainReadKind.ProjectionBatch)
                && request.CommandKind is null && request.CommandId == Guid.Empty
            || request.CommandKind is (OperationKind.OnlineTextPublicationPhase or OperationKind.CommitProjectionBatch)
                && request.ReadKind is null && request.CommandId != Guid.Empty);
}
