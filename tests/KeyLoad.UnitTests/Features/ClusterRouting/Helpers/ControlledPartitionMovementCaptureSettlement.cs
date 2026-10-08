using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Settles only the original actually admitted Capture after its native source owner joins release.</summary>
internal sealed class ControlledPartitionMovementCaptureSettlement
{
    private readonly ControlledPartitionMovementNode source;
    private readonly PartitionMovementTransportRequest original;
    private readonly string originalEnvelopeIdentity;

    internal ControlledPartitionMovementCaptureSettlement(ControlledPartitionMovementNode source,
        PartitionMovementTransportRequest actuallyVerified)
    {
        if (actuallyVerified.Action != PartitionMovementTransportAction.Capture
            || actuallyVerified.Envelope.Stage != PartitionMovePeerStage.Capture
            || actuallyVerified.Envelope.Grant is not { } grant
            || grant.PhaseCommandId != actuallyVerified.CommandId)
        { throw new InvalidOperationException("The actual originally admitted Capture identity is required."); }
        this.source = source;
        original = actuallyVerified;
        originalEnvelopeIdentity = JsonData.Fingerprint(actuallyVerified.Envelope with { Nonce = Guid.Empty });
    }

    internal Task<PartitionMovePhaseResult> SettleAsync(PartitionMovePeerEnvelope verified,
        CancellationToken cancellationToken)
    {
        if (!verified.Body.Span.SequenceEqual(original.Envelope.Body.Span)
            || JsonData.Fingerprint(verified with { Nonce = Guid.Empty }) != originalEnvelopeIdentity)
        { throw new InvalidOperationException("The original admitted Capture envelope must remain immutable."); }
        var principalId = PartitionMovementControlPrincipal.Resolve(source.Database, original.Envelope);
        var principal = source.Store.Read(view => source.Database.Principal(view, principalId,
            source.Database.EvaluationClock.GetUtcNow()));
        GrainRequestAuthority.RequireAdministrator(principal);
        var operation = source.Database.CreateVerifiedPartitionMovementOperation(original.CommandId,
            principal.Id, source.Database.EvaluationClock.GetUtcNow(), original.Envelope);
        var outcome = source.Journal.Submit(operation, cancellationToken);
        return Task.FromResult(outcome.Get<PartitionMovePhaseResult>());
    }
}
