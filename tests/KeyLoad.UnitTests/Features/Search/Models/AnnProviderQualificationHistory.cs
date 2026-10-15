namespace KeyLoad.UnitTests.Features.Search;

internal sealed record AnnProviderQualificationHistory(
    ReplicatedOperation Operation, OperationResult Result, long Index, long Term, byte[] Payload,
    byte[] OutcomeKey, byte[] OutcomeBytes, byte[] LocatorKey, byte[]? LocatorBytes);
