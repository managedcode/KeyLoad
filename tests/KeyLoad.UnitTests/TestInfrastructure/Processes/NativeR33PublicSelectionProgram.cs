namespace KeyLoad.UnitTests.Features.TestInfrastructure;

internal static class NativeR33PublicSelectionProgram
{
    internal const string Source = """
        const r33PublicArgs = [localArgs[0], "--KeyLoadTests:Filter=/*/*/(TypedEndpointJoinRf3Tests|DeclarativeCompositionReverseQuotaRf3Tests|SubscriptionPhasePublicColdTests|BlobSupersessionPublicTests|BlobPartialReadBoundaryPublicTests|NativeTextProtectedPublicColdTests|CurrentOwnershipTokenAfterMovementRf3Tests)/*", localArgs[2]];
        const r33Public = nativeSelection(r33PublicArgs, {});
        let r33PublicUnsupportedCount = 0;
        for (const badFilter of [
          "/*/*/SubscriptionPhasePublicColdTests/*",
          "/*/*/(DeclarativeCompositionReverseQuotaRf3Tests|SubscriptionPhasePublicColdTests|BlobSupersessionPublicTests|BlobPartialReadBoundaryPublicTests|NativeTextProtectedPublicColdTests|CurrentOwnershipTokenAfterMovementRf3Tests)/*",
          "/*/*/(TypedEndpointJoinRf3Tests|DeclarativeCompositionReverseQuotaRf3Tests|SubscriptionPhasePublicColdTests*|BlobSupersessionPublicTests|BlobPartialReadBoundaryPublicTests|NativeTextProtectedPublicColdTests|CurrentOwnershipTokenAfterMovementRf3Tests)/*",
          "/*/*/(DeclarativeCompositionReverseQuotaRf3Tests|TypedEndpointJoinRf3Tests|SubscriptionPhasePublicColdTests|BlobSupersessionPublicTests|BlobPartialReadBoundaryPublicTests|NativeTextProtectedPublicColdTests|CurrentOwnershipTokenAfterMovementRf3Tests)/*",
          "/*/*/(TypedEndpointJoinRf3Tests|DeclarativeCompositionReverseQuotaRf3Tests|SubscriptionPhasePublicColdTests|BlobSupersessionPublicTests|BlobPartialReadBoundaryPublicTests|NativeTextProtectedPublicColdTests|CurrentOwnershipTokenAfterMovementRf3Tests|OtherCase)/*",
          "/*/*/(TypedEndpointJoinRf3Tests|DeclarativeCompositionReverseQuotaRf3Tests|SubscriptionPhasePublicColdTests|BlobSupersessionPublicTests|BlobPartialReadBoundaryPublicTests|NativeTextProtectedPublicColdTests|CurrentOwnershipTokenAfterMovementRf3Tests)/* "]) {
          try { nativeSelection([r33PublicArgs[0], "--KeyLoadTests:Filter="+badFilter, r33PublicArgs[2]], {}); }
          catch (failure) {
            if (failure.message !== 'Invalid native local RF3 image selection.') throw failure;
            r33PublicUnsupportedCount++; continue;
          }
          throw new Error('Unsupported R33 public whole-class filter was accepted.');
        }
        """;
}
