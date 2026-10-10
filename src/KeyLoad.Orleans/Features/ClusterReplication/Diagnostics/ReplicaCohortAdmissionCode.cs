namespace KeyLoad.Orleans;

/// <summary>Closed native-local cohort evidence; never persisted or exchanged as authority.</summary>
public enum ReplicaCohortAdmissionCode
{
    /// <summary>The original prerequisite did not reach cohort evaluation.</summary>
    NotObserved = 0,
    /// <summary>The original node has no published native grain factory.</summary>
    GrainFactoryUnavailable = 1,
    /// <summary>The original node has no running native host.</summary>
    HostUnavailable = 2,
    /// <summary>The original discovery owner is stopping.</summary>
    DiscoveryStopping = 3,
    /// <summary>The actual local RPC or peer envelope contract is incompatible.</summary>
    LocalProtocolMismatch = 4,
    /// <summary>The actual local runtime-journal reader contract is incompatible.</summary>
    LocalReaderMismatch = 5,
    /// <summary>The actual local transport is unavailable.</summary>
    LocalTransportUnavailable = 6,
    /// <summary>The first actual fresh incompatible peer has a protocol mismatch.</summary>
    PeerProtocolMismatch = 7,
    /// <summary>The first actual fresh incompatible peer has a reader mismatch.</summary>
    PeerReaderMismatch = 8,
    /// <summary>The evaluated fresh compatible ready count is below the original majority.</summary>
    InsufficientFreshReady = 9,
    /// <summary>The original predicate admitted the compatible ready majority.</summary>
    Compatible = 10
}
