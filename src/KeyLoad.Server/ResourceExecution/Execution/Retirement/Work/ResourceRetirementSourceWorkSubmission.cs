namespace KeyLoad.Server.Features.ResourceExecution;

internal sealed record ResourceRetirementSourceWorkSubmission(ResourceRetirementStateV1 State,
    Exception? OriginalSubmissionFailure);
