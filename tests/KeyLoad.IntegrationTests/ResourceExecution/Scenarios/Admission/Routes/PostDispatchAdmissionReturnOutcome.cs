namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal sealed record PostDispatchAdmissionReturnOutcome(CommitReceipt Receipt, OutboxStatus Outbox);
