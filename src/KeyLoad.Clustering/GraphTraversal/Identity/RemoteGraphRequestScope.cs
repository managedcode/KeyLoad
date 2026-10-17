namespace KeyLoad.Clustering;

internal static class RemoteGraphRequestScope
{
    internal const string ParentPurpose = "keyload-remote-graph-parent-v1";
    internal const string ReadPurpose = "keyload-remote-graph-read-v1";
    internal const string SourcePurpose = "keyload-remote-graph-source-v1";
    internal const string ReceivePurpose = "keyload-remote-graph-receive-v1";
    internal const string CompletePurpose = "keyload-remote-graph-complete-v1";

    internal static bool HandlesRead(GrainReadKind? kind)
        => kind is GrainReadKind.RemoteGraphTargetPreparation or GrainReadKind.RemoteGraphSourceIntent
            or GrainReadKind.RemoteGraphReceiverReceipt or GrainReadKind.RemoteGraphIncoming
            or GrainReadKind.RemoteGraphReceiverCandidates or GrainReadKind.RemoteGraphIncomingFinalize;

    internal static bool HandlesParent(OperationKind? kind)
        => kind is OperationKind.UpsertRemoteGraphEdge or OperationKind.DeleteRemoteGraphEdge
            or OperationKind.DeliverRemoteGraphEdge;

    internal static string? PhasePurpose(OperationKind? kind) => kind switch
    {
        OperationKind.RemoteGraphSource => SourcePurpose,
        OperationKind.RemoteGraphReceive => ReceivePurpose,
        OperationKind.RemoteGraphComplete => CompletePurpose,
        _ => null
    };

    internal static bool Validate(GrainRequestEnvelope request)
    {
        var phase = PhasePurpose(request.CommandKind);
        var parent = HandlesParent(request.CommandKind) || request.ReadKind == GrainReadKind.RemoteGraphIncomingParent;
        var read = HandlesRead(request.ReadKind);
        var knownPurpose = request.Purpose is ParentPurpose or ReadPurpose or SourcePurpose or ReceivePurpose or CompletePurpose;
        if (!parent && !read && phase is null && !knownPurpose)
        {
            return false;
        }
        var expected = parent ? ParentPurpose : read ? ReadPurpose : phase;
        if (expected is null || request.Purpose != expected)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, GrainRoutingProtocol.AdministrationRequired);
        }
        if (read || request.ReadKind == GrainReadKind.RemoteGraphIncomingParent)
        {
            if (request.CommandKind is not null || request.CommandId != Guid.Empty)
            {
                throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
            }
        }
        else if (request.ReadKind is not null || request.CommandKind is null || request.CommandId == Guid.Empty)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        return true;
    }
}
