using KeyLoad.Clustering;
using KeyLoad.Core.Features.ResourceExecution;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ResourceExecution;

internal sealed class ResourceRetirementWorkAdmission(ResourceOwnerInventoryNativeTrust trust,
    IOptions<DatabaseLimits> limits, IOptions<GrainRoutingOptions> routing, TimeProvider clock)
{
    internal void Require(ResourceRetirementPhaseCommandV1 phase)
    {
        switch (phase.Phase)
        {
            case ResourceRetirementPhase.CommitOwnerWorkJoin:
                var body = NativeSerialization.Deserialize<ResourceRetirementCommitOwnerWorkJoinBodyV1>(phase.Body.Span);
                ResourceRetirementWorkEvidenceValidation.Require(body.Evidence, body.SealedState.Control,
                    body.Receiver, trust, limits, routing, clock);
                break;
            case ResourceRetirementPhase.RecordOwnerWorkJoin:
                var record = NativeSerialization.Deserialize<ResourceRetirementRecordOwnerWorkJoinBodyV1>(phase.Body.Span);
                ResourceRetirementWorkValidation.Owner(record.OwnerReceipt, record.Control, limits.Value);
                ResourceRetirementWorkEvidenceValidation.RequireDurable(record.OwnerReceipt, record.DurableReads,
                    trust, limits, routing, clock);
                break;
        }
    }
}
