namespace KeyLoad.Clustering;

internal interface IGrainRequestPurposeObserver
{
    string? BeforeSign(GrainRequestEnvelope original);

    void RejectedAfterMac(GrainRequestEnvelope actual, KeyLoadException failure, GrainRequestScopeCut cut);
}
