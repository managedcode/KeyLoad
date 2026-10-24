namespace KeyLoad.UnitTests.Search;

internal sealed record TextProviderAuditHistory(
    TextProtectedRebuildColdOwner.Retained Original, byte[] OutcomeKey, byte[] OutcomeBytes,
    byte[] LocatorKey, byte[]? LocatorBytes);
