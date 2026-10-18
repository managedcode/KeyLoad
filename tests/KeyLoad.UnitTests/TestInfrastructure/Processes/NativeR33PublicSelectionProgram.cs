namespace KeyLoad.UnitTests.Features.TestInfrastructure;

internal static class NativeR33PublicSelectionProgram
{
    internal const string Source = """
        const r33PublicArgs = [localArgs[0], "--KeyLoadTests:Filter=/*/*/(TypedEndpointJoinClusterTests|DeclarativeCompositionReverseQuotaClusterTests|SubscriptionPhasePublicColdTests|BlobSupersessionPublicTests|BlobPartialReadBoundaryPublicTests|NativeTextProtectedPublicColdTests)/*", localArgs[2]];
        const r33Public = nativeSelection(r33PublicArgs, {});
        let r33PublicUnsupportedCount = 0;
        for (const badFilter of [
          "/*/*/SubscriptionPhasePublicColdTests/*",
          "/*/*/(DeclarativeCompositionReverseQuotaClusterTests|SubscriptionPhasePublicColdTests|BlobSupersessionPublicTests|BlobPartialReadBoundaryPublicTests|NativeTextProtectedPublicColdTests)/*",
          "/*/*/(TypedEndpointJoinClusterTests|DeclarativeCompositionReverseQuotaClusterTests|SubscriptionPhasePublicColdTests*|BlobSupersessionPublicTests|BlobPartialReadBoundaryPublicTests|NativeTextProtectedPublicColdTests)/*",
          "/*/*/(DeclarativeCompositionReverseQuotaClusterTests|TypedEndpointJoinClusterTests|SubscriptionPhasePublicColdTests|BlobSupersessionPublicTests|BlobPartialReadBoundaryPublicTests|NativeTextProtectedPublicColdTests)/*",
          "/*/*/(TypedEndpointJoinClusterTests|DeclarativeCompositionReverseQuotaClusterTests|SubscriptionPhasePublicColdTests|BlobSupersessionPublicTests|BlobPartialReadBoundaryPublicTests|NativeTextProtectedPublicColdTests|OtherCase)/*",
          "/*/*/(TypedEndpointJoinClusterTests|DeclarativeCompositionReverseQuotaClusterTests|SubscriptionPhasePublicColdTests|BlobSupersessionPublicTests|BlobPartialReadBoundaryPublicTests|NativeTextProtectedPublicColdTests)/* "]) {
          try { nativeSelection([r33PublicArgs[0], "--KeyLoadTests:Filter="+badFilter, r33PublicArgs[2]], {}); }
          catch (failure) {
            if (failure.message !== 'Invalid native local CLUSTER image selection.') throw failure;
            r33PublicUnsupportedCount++; continue;
          }
          throw new Error('Unsupported R33 public whole-class filter was accepted.');
        }
        """;
}
