using ManagedCode.Communication;
namespace KeyLoad.FunctionalTests.Messaging;

internal sealed record SubscriptionContinuationOutcome(OperationKind Kind, object Request, Guid Id, Result<object> Result);
