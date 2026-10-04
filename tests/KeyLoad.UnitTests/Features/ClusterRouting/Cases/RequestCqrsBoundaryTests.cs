namespace KeyLoad.UnitTests.Features.ClusterRouting;

[RequestCqrsDataSource]
[NotInParallel]
internal sealed class RequestCqrsBoundaryTests(RequestCqrsClusterFixture fixture)
{
    [Test]
    public Task AcCrs006ActualClientAndSiloConvertersCarryPersistedPrincipalAndRequestState()
        => RequestCqrsIdentityCases.AcCrs006ActualClientAndSiloConvertersCarryPersistedPrincipalAndRequestState(fixture);

    [Test]
    public Task AcCrs006AbsentPrincipalIsAcceptedOnlyForSignedAuthentication()
        => RequestCqrsIdentityCases.AcCrs006AbsentPrincipalIsAcceptedOnlyForSignedAuthentication(fixture);

    [Test]
    public Task AcCrs006PresentNullPrincipalEntryIsNotAnonymousAuthentication()
        => RequestCqrsIdentityCases.AcCrs006PresentNullPrincipalEntryIsNotAnonymousAuthentication(fixture);

    [Test]
    [Arguments(RequestCqrsInvalidContext.MissingState)]
    [Arguments(RequestCqrsInvalidContext.MismatchedRequest)]
    [Arguments(RequestCqrsInvalidContext.MismatchedCommand)]
    [Arguments(RequestCqrsInvalidContext.MissingPrincipal)]
    [Arguments(RequestCqrsInvalidContext.ForgedSubject)]
    [Arguments(RequestCqrsInvalidContext.ExtraClaim)]
    [Arguments(RequestCqrsInvalidContext.SecondIdentity)]
    [Arguments(RequestCqrsInvalidContext.WrongAuthenticationType)]
    public Task AcCrs006InvalidNativeContextIsDeniedBeforeCapability(RequestCqrsInvalidContext invalid)
        => RequestCqrsIdentityCases.AcCrs006InvalidNativeContextIsDeniedBeforeCapability(fixture, invalid);

    [Test]
    public Task AcCrs006SignedRequestKeyAndForgedAuthenticationPrincipalAreDeniedBeforeCapability()
        => RequestCqrsIdentityCases.AcCrs006SignedRequestKeyAndForgedAuthenticationPrincipalAreDeniedBeforeCapability(fixture);

    [Test]
    public Task AcCrs006ScopePublishesOnlyPersistedIdentityAndRestoresEveryPriorKey()
        => RequestCqrsScopeCases.AcCrs006ScopePublishesOnlyPersistedIdentityAndRestoresEveryPriorKey(fixture);

    [Test]
    public Task AcCrs006FailedNativeAdmissionLeavesBothRequestContextKeysUntouched()
        => RequestCqrsScopeCases.AcCrs006FailedNativeAdmissionLeavesBothRequestContextKeysUntouched(fixture);

    [Test]
    public Task AcCrs006AuthenticationPublisherClearsPriorPrincipalAndRestoresIt()
        => RequestCqrsScopeCases.AcCrs006AuthenticationPublisherClearsPriorPrincipalAndRestoresIt(fixture);

    [Test]
    public Task AcCrs006ConcurrentPersistedScopesStayIsolatedThroughNativeCalls()
        => RequestCqrsScopeFlowCases.AcCrs006ConcurrentPersistedScopesStayIsolatedThroughNativeCalls(fixture);

    [Test]
    public Task AcCrs006PersistedScopeLivesThroughActualConsumerDisposal()
        => RequestCqrsScopeFlowCases.AcCrs006PersistedScopeLivesThroughActualConsumerDisposal(fixture);

    [Test]
    public Task AcCrs004ClosedProblemMapsEveryDefinedErrorCodeExactly()
        => RequestCqrsProtocolCases.AcCrs004ClosedProblemMapsEveryDefinedErrorCodeExactly();

    [Test]
    [Arguments(RequestCqrsProblemMutation.ExtraExtension)]
    [Arguments(RequestCqrsProblemMutation.NumericErrorCode)]
    [Arguments(RequestCqrsProblemMutation.UnknownErrorCode)]
    [Arguments(RequestCqrsProblemMutation.NumericEnumName)]
    [Arguments(RequestCqrsProblemMutation.WrongTitle)]
    [Arguments(RequestCqrsProblemMutation.WrongType)]
    [Arguments(RequestCqrsProblemMutation.WrongStatus)]
    [Arguments(RequestCqrsProblemMutation.Instance)]
    [Arguments(RequestCqrsProblemMutation.ExcessDetail)]
    public Task AcCrs004OpenProblemShapesFailClosed(RequestCqrsProblemMutation mutation)
        => RequestCqrsProtocolCases.AcCrs004OpenProblemShapesFailClosed(mutation);

    [Test]
    public Task AcCrs004ActualClientSerializerCounterIsInclusiveAndCancellationAware()
        => RequestCqrsProtocolCases.AcCrs004ActualClientSerializerCounterIsInclusiveAndCancellationAware(fixture);

    [Test]
    public Task AcCrs004ScratchAdmissionHonorsActualAdvanceNotReservationHint()
        => RequestCqrsProtocolCases.AcCrs004ScratchAdmissionHonorsActualAdvanceNotReservationHint();

    [Test]
    public Task AcCrs004NativeSixteenMiBReplyUsesActualRegisteredChunkSerializer()
        => RequestCqrsProtocolCases.AcCrs004NativeSixteenMiBReplyUsesActualRegisteredChunkSerializer(fixture);

    [Test]
    public Task AcCrs004AdmissionAcceptsOnlyTheTwoDefinedTerminalShapes()
        => RequestCqrsAdmissionCases.AcCrs004AdmissionAcceptsOnlyTheTwoDefinedTerminalShapes(fixture);

    [Test]
    [Arguments(RequestCqrsInvalidChunk.UnknownKind)]
    [Arguments(RequestCqrsInvalidChunk.WrongSequence)]
    [Arguments(RequestCqrsInvalidChunk.WrongEventType)]
    [Arguments(RequestCqrsInvalidChunk.ProgressKind)]
    [Arguments(RequestCqrsInvalidChunk.TerminalBeforeStarted)]
    [Arguments(RequestCqrsInvalidChunk.FailedWithSuccess)]
    [Arguments(RequestCqrsInvalidChunk.CompletedWithProblem)]
    [Arguments(RequestCqrsInvalidChunk.FailedWithValue)]
    [Arguments(RequestCqrsInvalidChunk.WrongStartedIdentity)]
    public Task AcCrs004MalformedNativeChunkIsRejectedBeforeAdmission(RequestCqrsInvalidChunk invalid)
        => RequestCqrsAdmissionCases.AcCrs004MalformedNativeChunkIsRejectedBeforeAdmission(fixture, invalid);

    [Test]
    public Task AcCrs004MissingTerminalAndCancellationCannotLookLikeCompletion()
        => RequestCqrsAdmissionCases.AcCrs004MissingTerminalAndCancellationCannotLookLikeCompletion(fixture);

    [Test]
    public Task AcCrs004CompletedReplyPayloadRemainsInsideItsExistingRawReplyBound()
        => RequestCqrsAdmissionCases.AcCrs004CompletedReplyPayloadRemainsInsideItsExistingRawReplyBound(fixture);

    [Test]
    public Task AcCrs003RealNativeProducerSettlesOnCancellation()
        => RequestCqrsLifecycleCases.AcCrs003RealNativeProducerSettlesOnCancellation(fixture);
}
