namespace KeyLoad.UnitTests.Features.DocumentStorage;

internal sealed class DocumentAuthorityIntersectionNativeTests
{
    [Test]
    public Task IndependentRowDisjunctionsIntersectBeforeReadsAndIndependentRepairsRemainHealthy()
        => RemoteDocumentNativeAssertions.RunAsync(DocumentAuthorityIntersectionNativeRows.RunAsync);

    [Test]
    public Task SourceAndTargetRawReadDenialsBothSurviveIndependentPolicyEpochsAndColdReads()
        => RemoteDocumentNativeAssertions.RunAsync(DocumentAuthorityIntersectionNativeFields.RunAsync);

    [Test]
    public Task RootArrayWildcardAndHiddenSchemaOmissionsRemainLiteralUnderBothNativePolicies()
        => RemoteDocumentNativeAssertions.RunAsync(DocumentAuthorityIntersectionNativeProjection.RunAsync);
}
