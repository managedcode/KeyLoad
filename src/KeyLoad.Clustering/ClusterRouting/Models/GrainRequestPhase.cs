namespace KeyLoad.Clustering;

/// <summary>Closed internal execution phases observable on an admitted native request.</summary>
internal enum GrainRequestPhase
{
    RequestStarted,
    AuthorizationReload,
    BeforeSubmit,
    SubmitReturned,
    ControlledDocumentGrantSettled,
    ControlledDocumentOutcomeReturned,
    RetireOperationSealed,
    ParentStagePreflight,
    TransferPageRetainedBudgetExceeded,
    TransferPageReturned,
    ParentTransferCloseFailed,
    ParentReceiverIssueAcknowledged,
    ParentReceiverIssueObserved,
    ParentFinalInstallPreflight,
    SampleChunkAdmissionPersisted,
    SampleChunkNativeJobReturned,
    SampleChunkAdmissionRefused,
    NativeTextOriginalPostingRead,
    DistributedSearchStatisticsCaptured,
    OnlineTextCaptured,
    EventVectorFirstDispatchReserved,
    EventVectorFirstCapturesReady,
    EventVectorFirstAdmitPrepared,
    MovementParentStateExecuting,
    MovementParentStateReturned,
    ResourceOwnerInventoryOwnerValidation,
    OriginalClaimReturned = 26,
    OriginalBusinessReplyReturned = 27
}
