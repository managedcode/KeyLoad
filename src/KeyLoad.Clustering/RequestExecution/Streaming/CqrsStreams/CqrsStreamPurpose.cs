namespace KeyLoad.Clustering;

/// <summary>Classifies one verified operation without retaining authority or changing execution.</summary>
internal sealed class CqrsStreamPurpose(Func<DecodedGrainRequest>? resolve = null)
{
    private RequestWorkLease? work;
    private Exception? admissionFailure;
    internal Exception? OriginalFailure
    {
        get; private set;
    }
    internal void RecordFailure(Exception error) => OriginalFailure = CapabilityWorkLifetime.CombinePrimary(OriginalFailure, error);
    internal RequestWorkLease WorkBinding => work
        ?? throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);

    internal void SetWork(RequestWorkLease original) => work = original;
    internal void AdmissionFailed(Exception original) => admissionFailure = original;
    internal void RequireAdmission() => RequestWorkSettlement.Rethrow(admissionFailure);

    private bool bound;
    private bool textCandidate;
    private bool annCandidate;
    private bool onlineCandidate;
    private DecodedGrainRequest? original;
    internal bool IsTextMaintenance
    {
        get; private set;
    }
    internal bool IsAnnMaintenance
    {
        get; private set;
    }
    internal bool IsOnlineTextMaintenance
    {
        get; private set;
    }
    internal bool HasLongProfile => IsTextMaintenance || IsAnnMaintenance
        || IsOnlineTextMaintenance;

    internal void Bind(DecodedGrainRequest request)
    {
        if (bound)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        }
        textCandidate = request.Envelope.CommandKind == OperationKind.MaintainTextIndex;
        annCandidate = request.Envelope.CommandKind == OperationKind.MaintainAnnIndex;
        onlineCandidate = request.Envelope.CommandKind == OperationKind.MaintainOnlineTextIndex;
        if (((textCandidate || annCandidate) && resolve is not null) || onlineCandidate)
        {
            original = request;
        }
        bound = true;
    }

    internal void EnableTextMaintenance(TextIndexMaintenanceMode mode)
    {
        if (!bound || !textCandidate)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        }
        IsTextMaintenance = mode is TextIndexMaintenanceMode.Build or TextIndexMaintenanceMode.Restore;
    }

    internal void EnableAnnMaintenance(AnnMaintenanceMode mode)
    {
        if (!bound || !annCandidate)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        }
        IsAnnMaintenance = mode is AnnMaintenanceMode.Build or AnnMaintenanceMode.Restore;
    }

    internal void ResolveProgress()
    {
        if (original is not { } request)
        {
            return;
        }
        original = null;
        if (onlineCandidate)
        {
            _ = GrainPayload.ReadCommand<OnlineTextIndexMaintenanceRequest>(request.Payload);
            IsOnlineTextMaintenance = true;
            return;
        }
        if (annCandidate)
        {
            var ann = GrainPayload.ReadCommand<AnnMaintenanceRequest>(request.Payload);
            EnableAnnMaintenance(ann.Mode);
            return;
        }
        var maintenance = GrainPayload.ReadCommand<TextIndexMaintenanceRequest>(request.Payload);
        EnableTextMaintenance(maintenance.Mode);
    }

    internal void Started()
    {
        if (!bound && resolve is not null)
        {
            Bind(resolve());
        }
    }
}
