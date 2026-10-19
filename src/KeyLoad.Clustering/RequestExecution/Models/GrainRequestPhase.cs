namespace KeyLoad.Clustering;

/// <summary>Closed internal execution phases observable on an admitted execution request.</summary>
internal enum GrainRequestPhase
{
    RequestStarted,
    AuthorizationReload,
    BeforeSubmit,
    SubmitReturned,
    SampleChunkAdmissionPersisted = 14,
    SampleChunkJobReturned,
    SampleChunkAdmissionRefused,
    TextOriginalPostingRead,
    DistributedSearchStatisticsCaptured,
    OnlineTextCaptured,
    OriginalClaimReturned = 26,
    OriginalBusinessReplyReturned = 27
}
