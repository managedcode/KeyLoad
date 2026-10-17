using System.Collections.Immutable;

namespace KeyLoad.Server.Features.ResourceExecution;

internal sealed record ResourceRetirementWorkCaptureResult(RegisteredPhysicalOwnerV1 Owner,
    ImmutableArray<ResourceRetirementWorkJoinEvidenceV1> Evidence, ResourceRetirementWorkOwnerReceiptV1? Acknowledged);
