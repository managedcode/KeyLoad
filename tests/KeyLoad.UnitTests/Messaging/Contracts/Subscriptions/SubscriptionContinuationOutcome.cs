using ManagedCode.Communication;
namespace KeyLoad.UnitTests.Messaging;

internal sealed record SubscriptionContinuationOutcome(OperationKind Kind, object Request, Guid Id, Result<object> Result);
