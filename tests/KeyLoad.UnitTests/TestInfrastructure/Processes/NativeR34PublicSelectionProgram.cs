namespace KeyLoad.UnitTests.Features.TestInfrastructure;

internal static class NativeR34PublicSelectionProgram
{
    internal const string Source = """
        const r34PublicArgs = [localArgs[0], "--KeyLoadTests:Filter=/*/*/(TypedEndpointJoinClusterTests|DeclarativeCompositionReverseQuotaClusterTests|SubscriptionPhasePublicColdTests|BlobSupersessionPublicTests|BlobPartialReadBoundaryPublicTests|NativeTextProtectedPublicColdTests|QueueLifecyclePublicClusterTests|BlobBoundedRetirementPublicTests|BlobReadPolicyPublicTests|BlobReservationQuotaPublicTests|NativeTextNestedWildcardPublicTests|NativeTextRawReadPolicyPublicTests|NativeTextRequestRefusalPublicTests|NativeTextRowOwnershipPublicTests)/*", localArgs[2]];
        const r34Public = nativeSelection(r34PublicArgs, {});
        let r34PublicUnsupportedCount = 0;
        for (const badFilter of [
          "/*/*/QueueLifecyclePublicClusterTests/*",
          "/*/*/(DeclarativeCompositionReverseQuotaClusterTests|SubscriptionPhasePublicColdTests|BlobSupersessionPublicTests|BlobPartialReadBoundaryPublicTests|NativeTextProtectedPublicColdTests|QueueLifecyclePublicClusterTests|BlobBoundedRetirementPublicTests|BlobReadPolicyPublicTests|BlobReservationQuotaPublicTests|NativeTextNestedWildcardPublicTests|NativeTextRawReadPolicyPublicTests|NativeTextRequestRefusalPublicTests|NativeTextRowOwnershipPublicTests)/*",
          "/*/*/(TypedEndpointJoinClusterTests|DeclarativeCompositionReverseQuotaClusterTests|SubscriptionPhasePublicColdTests|BlobSupersessionPublicTests|BlobPartialReadBoundaryPublicTests|NativeTextProtectedPublicColdTests|QueueLifecyclePublicClusterTests*|BlobBoundedRetirementPublicTests|BlobReadPolicyPublicTests|BlobReservationQuotaPublicTests|NativeTextNestedWildcardPublicTests|NativeTextRawReadPolicyPublicTests|NativeTextRequestRefusalPublicTests|NativeTextRowOwnershipPublicTests)/*",
          "/*/*/(DeclarativeCompositionReverseQuotaClusterTests|TypedEndpointJoinClusterTests|SubscriptionPhasePublicColdTests|BlobSupersessionPublicTests|BlobPartialReadBoundaryPublicTests|NativeTextProtectedPublicColdTests|QueueLifecyclePublicClusterTests|BlobBoundedRetirementPublicTests|BlobReadPolicyPublicTests|BlobReservationQuotaPublicTests|NativeTextNestedWildcardPublicTests|NativeTextRawReadPolicyPublicTests|NativeTextRequestRefusalPublicTests|NativeTextRowOwnershipPublicTests)/*",
          "/*/*/(TypedEndpointJoinClusterTests|DeclarativeCompositionReverseQuotaClusterTests|SubscriptionPhasePublicColdTests|BlobSupersessionPublicTests|BlobPartialReadBoundaryPublicTests|NativeTextProtectedPublicColdTests|QueueLifecyclePublicClusterTests|BlobBoundedRetirementPublicTests|BlobReadPolicyPublicTests|BlobReservationQuotaPublicTests|NativeTextNestedWildcardPublicTests|NativeTextRawReadPolicyPublicTests|NativeTextRequestRefusalPublicTests|NativeTextRowOwnershipPublicTests|OtherCase)/*",
          "/*/*/(TypedEndpointJoinClusterTests|DeclarativeCompositionReverseQuotaClusterTests|SubscriptionPhasePublicColdTests|BlobSupersessionPublicTests|BlobPartialReadBoundaryPublicTests|NativeTextProtectedPublicColdTests|QueueLifecyclePublicClusterTests|BlobBoundedRetirementPublicTests|BlobReadPolicyPublicTests|BlobReservationQuotaPublicTests|NativeTextNestedWildcardPublicTests|NativeTextRawReadPolicyPublicTests|NativeTextRequestRefusalPublicTests|NativeTextRowOwnershipPublicTests)/* "]) {
          try { nativeSelection([r34PublicArgs[0], "--KeyLoadTests:Filter="+badFilter, r34PublicArgs[2]], {}); }
          catch (failure) {
            if (failure.message !== 'Invalid native local CLUSTER image selection.') throw failure;
            r34PublicUnsupportedCount++; continue;
          }
          throw new Error('Unsupported R34 public whole-class filter was accepted.');
        }
        """;

    internal const string SuccessorSource = """
        const r35PublicArgs = [localArgs[0], "--KeyLoadTests:Filter=/*/*/(TypedEndpointJoinClusterTests|DeclarativeCompositionReverseQuotaClusterTests|SubscriptionPhasePublicColdTests|BlobSupersessionPublicTests|BlobPartialReadBoundaryPublicTests|NativeTextProtectedPublicColdTests|QueueLifecyclePublicClusterTests|BlobBoundedRetirementPublicTests|BlobReadPolicyPublicTests|BlobReservationQuotaPublicTests|NativeTextNestedWildcardPublicTests|NativeTextRawReadPolicyPublicTests|NativeTextRequestRefusalPublicTests|NativeTextRowOwnershipPublicTests|GraphVertexPublicWholeFlowTests|QueueLeaseFencingClusterTests|PostgresNativeSessionClusterTests|BlobRowCreatorPublicTests|BlobRejectedContentPublicTests|BlobListRowPolicyPublicTests|NativeTextNormalizationPublicTests|NativeTextDistinctTermsPublicTests)/*", localArgs[2]];
        const r35Public = nativeSelection(r35PublicArgs, {});
        let r35PublicUnsupportedCount = 0;
        for (const badFilter of [
          "/*/*/GraphVertexPublicWholeFlowTests/*",
          "/*/*/(TypedEndpointJoinClusterTests|DeclarativeCompositionReverseQuotaClusterTests|SubscriptionPhasePublicColdTests|BlobSupersessionPublicTests|BlobPartialReadBoundaryPublicTests|NativeTextProtectedPublicColdTests|QueueLifecyclePublicClusterTests|BlobBoundedRetirementPublicTests|BlobReadPolicyPublicTests|BlobReservationQuotaPublicTests|NativeTextNestedWildcardPublicTests|NativeTextRawReadPolicyPublicTests|NativeTextRequestRefusalPublicTests|NativeTextRowOwnershipPublicTests|QueueLeaseFencingClusterTests|PostgresNativeSessionClusterTests|BlobRowCreatorPublicTests|BlobRejectedContentPublicTests|BlobListRowPolicyPublicTests|NativeTextNormalizationPublicTests|NativeTextDistinctTermsPublicTests)/*",
          "/*/*/(TypedEndpointJoinClusterTests|DeclarativeCompositionReverseQuotaClusterTests|SubscriptionPhasePublicColdTests|BlobSupersessionPublicTests|BlobPartialReadBoundaryPublicTests|NativeTextProtectedPublicColdTests|QueueLifecyclePublicClusterTests|BlobBoundedRetirementPublicTests|BlobReadPolicyPublicTests|BlobReservationQuotaPublicTests|NativeTextNestedWildcardPublicTests|NativeTextRawReadPolicyPublicTests|NativeTextRequestRefusalPublicTests|NativeTextRowOwnershipPublicTests|GraphVertexPublicWholeFlowTests*|QueueLeaseFencingClusterTests|PostgresNativeSessionClusterTests|BlobRowCreatorPublicTests|BlobRejectedContentPublicTests|BlobListRowPolicyPublicTests|NativeTextNormalizationPublicTests|NativeTextDistinctTermsPublicTests)/*",
          "/*/*/(DeclarativeCompositionReverseQuotaClusterTests|TypedEndpointJoinClusterTests|SubscriptionPhasePublicColdTests|BlobSupersessionPublicTests|BlobPartialReadBoundaryPublicTests|NativeTextProtectedPublicColdTests|QueueLifecyclePublicClusterTests|BlobBoundedRetirementPublicTests|BlobReadPolicyPublicTests|BlobReservationQuotaPublicTests|NativeTextNestedWildcardPublicTests|NativeTextRawReadPolicyPublicTests|NativeTextRequestRefusalPublicTests|NativeTextRowOwnershipPublicTests|GraphVertexPublicWholeFlowTests|QueueLeaseFencingClusterTests|PostgresNativeSessionClusterTests|BlobRowCreatorPublicTests|BlobRejectedContentPublicTests|BlobListRowPolicyPublicTests|NativeTextNormalizationPublicTests|NativeTextDistinctTermsPublicTests)/*",
          "/*/*/(TypedEndpointJoinClusterTests|DeclarativeCompositionReverseQuotaClusterTests|SubscriptionPhasePublicColdTests|BlobSupersessionPublicTests|BlobPartialReadBoundaryPublicTests|NativeTextProtectedPublicColdTests|QueueLifecyclePublicClusterTests|BlobBoundedRetirementPublicTests|BlobReadPolicyPublicTests|BlobReservationQuotaPublicTests|NativeTextNestedWildcardPublicTests|NativeTextRawReadPolicyPublicTests|NativeTextRequestRefusalPublicTests|NativeTextRowOwnershipPublicTests|GraphVertexPublicWholeFlowTests|QueueLeaseFencingClusterTests|PostgresNativeSessionClusterTests|BlobRowCreatorPublicTests|BlobRejectedContentPublicTests|BlobListRowPolicyPublicTests|NativeTextNormalizationPublicTests|NativeTextDistinctTermsPublicTests|OtherCase)/*",
          "/*/*/(TypedEndpointJoinClusterTests|DeclarativeCompositionReverseQuotaClusterTests|SubscriptionPhasePublicColdTests|BlobSupersessionPublicTests|BlobPartialReadBoundaryPublicTests|NativeTextProtectedPublicColdTests|QueueLifecyclePublicClusterTests|BlobBoundedRetirementPublicTests|BlobReadPolicyPublicTests|BlobReservationQuotaPublicTests|NativeTextNestedWildcardPublicTests|NativeTextRawReadPolicyPublicTests|NativeTextRequestRefusalPublicTests|NativeTextRowOwnershipPublicTests|GraphVertexPublicWholeFlowTests|QueueLeaseFencingClusterTests|PostgresNativeSessionClusterTests|BlobRowCreatorPublicTests|BlobRejectedContentPublicTests|BlobListRowPolicyPublicTests|NativeTextNormalizationPublicTests|NativeTextDistinctTermsPublicTests)/* "]) {
          try { nativeSelection([r35PublicArgs[0], "--KeyLoadTests:Filter="+badFilter, r35PublicArgs[2]], {}); }
          catch (failure) {
            if (failure.message !== 'Invalid native local CLUSTER image selection.') throw failure;
            r35PublicUnsupportedCount++; continue;
          }
          throw new Error('Unsupported R35 public whole-class filter was accepted.');
        }
        """;

}
