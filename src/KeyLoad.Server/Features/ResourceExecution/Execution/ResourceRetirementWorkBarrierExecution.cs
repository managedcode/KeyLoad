using System.Collections.Immutable;

namespace KeyLoad.Server.Features.ResourceExecution;

/// <summary>Local original-operation report; exceptions are neither persisted nor serialized.</summary>
internal sealed record ResourceRetirementWorkBarrierExecution(ResourceRetirementWorkBarrierReceiptV1 Receipt,
    ImmutableArray<Exception> OriginalSubmissionFailures);
