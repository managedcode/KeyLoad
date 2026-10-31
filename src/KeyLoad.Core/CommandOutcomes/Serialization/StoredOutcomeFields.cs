namespace KeyLoad.Core.InternalSerialization;

internal static class StoredOutcomeFields
{
    // Field 0 was the command fingerprint. It is retired and must never be reused.
    internal const uint Incarnation = 1;
    internal const uint PolicyEpoch = 2;
    internal const uint Result = 3;
    internal const uint CompositionAuthority = 5;
    internal const uint ScopeKind = 6;
    internal const uint Partition = 7;
    internal const uint OnlineTextAuthority = 8;
    internal const uint RemoteTransferFailureAuthority = 9;
    internal const uint CanonicalSequence = 11;
    internal const uint PreparedCommand = 12;
}
