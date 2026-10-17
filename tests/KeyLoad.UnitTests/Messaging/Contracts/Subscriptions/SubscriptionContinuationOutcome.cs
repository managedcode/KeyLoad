namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed record SubscriptionContinuationOutcome(OperationKind Kind, object Request, Guid Id, OperationResult Result);
