using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Core.Features.ClusterRouting.Validation;

internal static class PartitionMoveResourceBinding
{
    internal static void Require(PartitionMovePeerEnvelope envelope)
    {
        if (envelope.Stage == PartitionMovePeerStage.PublishWitness)
        {
            var body = NativeSerialization.Deserialize<PartitionMovePublishBody>(envelope.Body.Span);
            if (envelope.Grant is null || body.Resources.IsDefault || envelope.Grant.Resources.IsDefault
                || JsonData.Fingerprint(body.Resources) != JsonData.Fingerprint(envelope.Grant.Resources))
            { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
            return;
        }
        var descriptor = envelope.Stage switch
        {
            PartitionMovePeerStage.StagePage => NativeSerialization.Deserialize<PartitionMovePageBody>(envelope.Body.Span).Descriptor,
            PartitionMovePeerStage.Install => NativeSerialization.Deserialize<PartitionMoveInstallBody>(envelope.Body.Span).Descriptor,
            _ => null
        };
        if (descriptor is null)
        { return; }
        var grant = envelope.Grant
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.MissingAuthority);
        if (grant.Resources.IsDefault || descriptor.Resources.IsDefault
            || JsonData.Fingerprint(grant.Resources) != JsonData.Fingerprint(descriptor.Resources))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
    }
}
