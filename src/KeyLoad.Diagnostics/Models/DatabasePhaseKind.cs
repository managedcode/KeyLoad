namespace KeyLoad.Diagnostics.Features.ResourceExecution;

/// <summary>Closed source-owned database timing boundaries; IDs are stable.</summary>
public enum DatabasePhaseKind
{
    /// <summary>The credential resolver's real read dispatch.</summary>
    PublicAuthenticationDispatch = 0,
    /// <summary>The operation gateway's real node dispatch.</summary>
    PublicOperationDispatch = 1,
    /// <summary>The authorized authentication branch's quorum barrier.</summary>
    AuthorizedAuthenticationBarrier = 2,
    /// <summary>The authorized operation branch's separate quorum barrier.</summary>
    AuthorizedOperationBarrier = 3,
    /// <summary>The authorized read capability invocation.</summary>
    AuthorizedReadCapability = 4,
    /// <summary>The read executor's transport readiness wait.</summary>
    ReadTransportReady = 5,
    /// <summary>The complete leader read barrier including apply.</summary>
    LeaderBarrierTotal = 6,
    /// <summary>The read round's real semaphore acquisition wait.</summary>
    ReadRoundsWait = 7,
    /// <summary>The read round's held semaphore before apply.</summary>
    ReadRoundsHold = 8,
    /// <summary>The submit round's real semaphore acquisition wait.</summary>
    SubmitRoundsWait = 9,
    /// <summary>The submit semaphore held through apply and outcome.</summary>
    SubmitRoundsHold = 10,
    /// <summary>An admitted heartbeat's real held semaphore.</summary>
    HeartbeatRoundsHold = 11,
    /// <summary>The majority loop; outstanding original tails are independent.</summary>
    QuorumFollowerAwait = 12,
    /// <summary>The real round finalizer; it is not always a durable commit.</summary>
    RoundCommitFinalize = 13,
    /// <summary>One original follower sender through its own release.</summary>
    FollowerSynchronization = 14,
    /// <summary>The actual replica protocol semaphore acquisition wait.</summary>
    ReplicaProtocolGateWait = 15,
    /// <summary>The actual replica protocol semaphore hold.</summary>
    ReplicaProtocolGateHold = 16,
    /// <summary>Original typed request serialization and payload-cap check.</summary>
    ReplicaRequestEncode = 17,
    /// <summary>Original invocation with UTF8 argument conversion under its deadline.</summary>
    ReplicaTransportAwait = 18,
    /// <summary>Existing strict reply decoding.</summary>
    ReplicaReplyDecode = 19,
    /// <summary>The complete native replica endpoint resolution.</summary>
    ReplicaDiscoveryResolve = 20,
    /// <summary>The Orleans Exchange WaitAsync wrapper, not the original tail.</summary>
    OrleansReplicaExchangeAwait = 21,
    /// <summary>The real receiver's reply or error.</summary>
    ReplicaReceiverTotal = 22,
    /// <summary>The original canonical materializer watermark wait.</summary>
    CanonicalApplyWait = 23,
    /// <summary>The actual canonical apply or maintenance gate acquisition.</summary>
    CanonicalApplyGateWait = 24,
    /// <summary>The acquired canonical apply or maintenance gate hold.</summary>
    CanonicalApplyGateHold = 25,
    /// <summary>The existing scheduling batch including per-entry commits.</summary>
    CanonicalApplyBatch = 26,
    /// <summary>The provider's actual Read reader-lock acquisition.</summary>
    ProviderReadGateWait = 27,
    /// <summary>The provider's actual Commit writer-lock acquisition.</summary>
    ProviderWriteGateWait = 28,
    /// <summary>The provider Commit writer-lock hold, including no-change calls.</summary>
    ProviderWriteGateHold = 29,
    /// <summary>Existing atomic redo writes through their Flush(true).</summary>
    AtomicJournalWriteFlush = 30,
    /// <summary>Actual cache/value/native Sync-WAL/tree mutation work.</summary>
    NativeTreeMutation = 31
}
