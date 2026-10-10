namespace KeyLoad.Core.Features.Search;

internal static class OnlineTextPublicationValidation
{
    internal static void LocalSource(OnlineTextPublicationPhaseCommand command,
        NativeTextSeedCapture current, NativeTextSeedCapture validated)
    {
        var source = command.Result.PublishedCut;
        if (command.Request.CommandId != command.Result.CommandId || command.SessionId == Guid.Empty
            || command.GenerationId == Guid.Empty || command.Request.Consumer != command.Result.Consumer
            || command.Request.ConsumerGeneration != command.Result.ConsumerGeneration
            || command.Request.NodeId != source.NodeId || command.DataEpoch != current.DataEpoch
            || source.Incarnation != current.Incarnation || source.Position != current.Position
            || source.AppliedPosition != current.AppliedPosition || source.ReadGeneration != current.ReadGeneration
            || source.ThroughSequence != current.UpperSequence || source.PolicyEpoch != current.PolicyEpoch
            || source.SchemaVersion != current.SchemaVersion || source.ResourceSha256 != current.ResourceSha256
            || current.PrincipalId != validated.PrincipalId || current.PolicyEpoch != validated.PolicyEpoch
            || current.SchemaVersion != validated.SchemaVersion || current.Position != validated.Position
            || current.AppliedPosition != validated.AppliedPosition || current.UpperSequence != validated.UpperSequence
            || current.FirstAvailableSequence != validated.FirstAvailableSequence || current.Checkpoint != validated.Checkpoint
            || current.Incarnation != validated.Incarnation || current.DataEpoch != validated.DataEpoch
            || current.ReadGeneration != validated.ReadGeneration || current.ResourceSha256 != validated.ResourceSha256)
        { throw Errors.Fail(ErrorCode.OwnershipLost, OnlineTextPublicationProtocol.InvalidPublication); }
    }

    internal static OnlineTextOutcomeAuthority Authority(string principalId, OnlineTextPublicationPhaseCommand command)
        => new(OnlineTextParentIdentity.Fingerprint(principalId, command.Request),
            command.Request.Collection, command.Request.Field, command.DataEpoch, command.Request.Placement,
            command.Leaf, command.ManifestSha256, command.GenerationId, command.ExpectedCurrentCommandId);
}
