namespace KeyLoad.Server.Features.ResourceExecution;

internal sealed record ResourceRetirementOwnerWorkSubmission(ResourceRetirementWorkOwnerReceiptV1 Receipt,
    ResourceRetirementWorkCaptureResult? DurableRead, Exception? OriginalSubmissionFailure);
