namespace KeyLoad.Clustering;

internal interface IGrainRequestPurposeObserver
{
    string? BeforeIssue(GrainRequestEnvelope original);

    void RejectedAfterDecode(GrainRequestEnvelope actual, KeyLoadException failure, GrainRequestScopeCut cut);
}
