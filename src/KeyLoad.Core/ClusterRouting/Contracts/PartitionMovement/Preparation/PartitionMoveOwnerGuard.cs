namespace KeyLoad.Core;

internal enum PartitionMoveOwnerGuard
{
    Unobserved,
    DispatchIdentity,
    DispatchExpiry,
    ConfiguredReceiver,
    ReceiverPhysicalShard,
    ReceiverStoreIncarnation,
    ReceiverCatalogIncarnation,
    ReceiverAbortScope,
    LocalIssuance,
    LocalOriginalPhase,
    LocalGrant,
    LocalPending,
    LocalControlOwner,
    LocalPhaseBytes,
    PrepareControlOwner,
    PrepareStage,
    PrepareOriginalPhase,
    PrepareIssuance,
    PrepareCommand,
    PreparePhaseBytes,
    CancellationPhase,
}
