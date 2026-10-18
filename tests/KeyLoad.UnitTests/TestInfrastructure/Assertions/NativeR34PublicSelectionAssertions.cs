using System.Text.Json;

namespace KeyLoad.UnitTests.Features.TestInfrastructure;

internal static class NativeR34PublicSelectionAssertions
{
    private const string SelectedProperty = "r34Public";
    private const string RejectedProperty = "r34PublicUnsupportedCount";
    private const int ExpectedRefusals = 6;
    private const string SuccessorSelectedProperty = "r35Public";
    private const string SuccessorRejectedProperty = "r35PublicUnsupportedCount";
    private const string Filter = "/*/*/(TypedEndpointJoinClusterTests|DeclarativeCompositionReverseQuotaClusterTests|SubscriptionPhasePublicColdTests|BlobSupersessionPublicTests|BlobPartialReadBoundaryPublicTests|NativeTextProtectedPublicColdTests|QueueLifecyclePublicClusterTests|BlobBoundedRetirementPublicTests|BlobReadPolicyPublicTests|BlobReservationQuotaPublicTests|NativeTextNestedWildcardPublicTests|NativeTextRawReadPolicyPublicTests|NativeTextRequestRefusalPublicTests|NativeTextRowOwnershipPublicTests)/*";

    internal static async Task AssertSuccessorAsync(JsonElement root)
    {
        await NativeTUnitSelectionTests.AssertLocalSelectorAsync(root.GetProperty(SuccessorSelectedProperty),
            "/*/*/(TypedEndpointJoinClusterTests|DeclarativeCompositionReverseQuotaClusterTests|SubscriptionPhasePublicColdTests|BlobSupersessionPublicTests|BlobPartialReadBoundaryPublicTests|NativeTextProtectedPublicColdTests|QueueLifecyclePublicClusterTests|BlobBoundedRetirementPublicTests|BlobReadPolicyPublicTests|BlobReservationQuotaPublicTests|NativeTextNestedWildcardPublicTests|NativeTextRawReadPolicyPublicTests|NativeTextRequestRefusalPublicTests|NativeTextRowOwnershipPublicTests|GraphVertexPublicWholeFlowTests|QueueLeaseFencingClusterTests|PostgresNativeSessionClusterTests|BlobRowCreatorPublicTests|BlobRejectedContentPublicTests|BlobListRowPolicyPublicTests|NativeTextNormalizationPublicTests|NativeTextDistinctTermsPublicTests)/*").ConfigureAwait(false);
        await Assert.That(root.GetProperty(SuccessorRejectedProperty).GetInt32()).IsEqualTo(ExpectedRefusals);
    }

    internal static async Task AssertAsync(JsonElement root)
    {
        await NativeTUnitSelectionTests.AssertLocalSelectorAsync(root.GetProperty(SelectedProperty), Filter).ConfigureAwait(false);
        await Assert.That(root.GetProperty(RejectedProperty).GetInt32()).IsEqualTo(ExpectedRefusals);
    }
}
