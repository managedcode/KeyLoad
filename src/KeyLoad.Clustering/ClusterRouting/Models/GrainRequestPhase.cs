namespace KeyLoad.Clustering;

/// <summary>Closed internal execution phases observable on an admitted native request.</summary>
internal enum GrainRequestPhase
{
    RequestStarted,
    AuthorizationReload,
    BeforeSubmit,
    SubmitReturned,
    SampleChunkAdmissionPersisted = 14,
    SampleChunkNativeJobReturned,
    SampleChunkAdmissionRefused,
    NativeTextOriginalPostingRead,
    DistributedSearchStatisticsCaptured,
    OnlineTextCaptured,
    OriginalClaimReturned = 26,
    OriginalBusinessReplyReturned = 27
}
