namespace KeyLoad.Orleans;

/// <summary>Classifies one verified signed operation without retaining authority or changing execution.</summary>
internal sealed class NativeCqrsStreamPurpose(Func<DecodedGrainRequest>? resolve = null)
{
    private bool bound;
    private bool textCandidate;
    private bool annCandidate;
    private bool tailCandidate;
    private DecodedGrainRequest? original;
    internal bool IsTextMaintenance { get; private set; }
    internal bool IsAnnMaintenance { get; private set; }
    internal EventFeedControlRequest? TailRequest { get; private set; }
    internal bool IsEventFeedTail => TailRequest is not null;
    internal bool HasLongProfile => IsTextMaintenance || IsAnnMaintenance || IsEventFeedTail;

    internal void Bind(DecodedGrainRequest request)
    {
        if (bound)
        { throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest); }
        textCandidate = request.Envelope.CommandKind == OperationKind.MaintainTextIndex;
        annCandidate = request.Envelope.CommandKind == OperationKind.MaintainAnnIndex;
        tailCandidate = request.Envelope.CommandKind == OperationKind.EventFeedControl
            && request.Envelope.Purpose == EventVectorRequestScope.ParentPurpose;
        if (((textCandidate || annCandidate) && resolve is not null) || tailCandidate)
        { original = request; }
        bound = true;
    }

    internal void EnableTextMaintenance(TextIndexMaintenanceMode mode)
    {
        if (!bound || !textCandidate)
        { throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest); }
        IsTextMaintenance = mode is TextIndexMaintenanceMode.Build or TextIndexMaintenanceMode.Restore;
    }

    internal void EnableAnnMaintenance(AnnMaintenanceMode mode)
    {
        if (!bound || !annCandidate)
        { throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest); }
        IsAnnMaintenance = mode is AnnMaintenanceMode.Build or AnnMaintenanceMode.Restore;
    }

    internal void ResolveProgress()
    {
        if (original is not { } request)
        { return; }
        original = null;
        if (tailCandidate)
        {
            var tail = GrainNativePayload.ReadCommand<EventFeedControlRequest>(request.Payload);
            if (tail.Action == EventFeedControlAction.Tail)
            { TailRequest = tail; }
            return;
        }
        if (annCandidate)
        {
            var ann = GrainNativePayload.ReadCommand<AnnMaintenanceRequest>(request.Payload);
            EnableAnnMaintenance(ann.Mode);
            return;
        }
        var maintenance = GrainNativePayload.ReadCommand<TextIndexMaintenanceRequest>(request.Payload);
        EnableTextMaintenance(maintenance.Mode);
    }

    internal void Started()
    {
        if (!bound && resolve is not null)
        { Bind(resolve()); }
    }
}
