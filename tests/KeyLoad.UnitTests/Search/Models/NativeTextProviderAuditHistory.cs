namespace KeyLoad.UnitTests.Features.Search;

internal sealed record NativeTextProviderAuditHistory(
    NativeTextProtectedRebuildColdOwner.Retained Original, byte[] OutcomeKey, byte[] OutcomeBytes,
    byte[] LocatorKey, byte[]? LocatorBytes);
