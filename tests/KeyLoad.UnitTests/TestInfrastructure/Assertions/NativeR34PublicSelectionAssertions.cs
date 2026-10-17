using System.Text.Json;

namespace KeyLoad.UnitTests.Features.TestInfrastructure;

internal static class NativeR34PublicSelectionAssertions
{
    private const string SelectedProperty = "r34Public";
    private const string RejectedProperty = "r34PublicUnsupportedCount";
    private const int ExpectedRefusals = 6;
    private const string SuccessorSelectedProperty = "r35Public";
    private const string SuccessorRejectedProperty = "r35PublicUnsupportedCount";
    private const string Filter = "/*/*/(TypedEndpointJoinRf3Tests|DeclarativeCompositionReverseQuotaRf3Tests|SubscriptionPhasePublicColdTests|BlobSupersessionPublicTests|BlobPartialReadBoundaryPublicTests|NativeTextProtectedPublicColdTests|CurrentOwnershipTokenAfterMovementRf3Tests|QueueLifecyclePublicRf3Tests|PhysicalShardPartitionMovementRf3Tests|TenantPackingActivationRf3Tests|BlobBoundedRetirementPublicTests|BlobPublicCancellationTests|BlobReadPolicyPublicTests|BlobReservationQuotaPublicTests|NativeTextNestedWildcardPublicTests|NativeTextRawReadPolicyPublicTests|NativeTextRequestRefusalPublicTests|NativeTextRowOwnershipPublicTests)/*";

    internal static async Task AssertSuccessorAsync(JsonElement root)
    {
        await NativeTUnitSelectionTests.AssertLocalSelectorAsync(root.GetProperty(SuccessorSelectedProperty),
            "/*/*/(TypedEndpointJoinRf3Tests|DeclarativeCompositionReverseQuotaRf3Tests|SubscriptionPhasePublicColdTests|BlobSupersessionPublicTests|BlobPartialReadBoundaryPublicTests|NativeTextProtectedPublicColdTests|CurrentOwnershipTokenAfterMovementRf3Tests|QueueLifecyclePublicRf3Tests|PhysicalShardPartitionMovementRf3Tests|TenantPackingActivationRf3Tests|BlobBoundedRetirementPublicTests|BlobPublicCancellationTests|BlobReadPolicyPublicTests|BlobReservationQuotaPublicTests|NativeTextNestedWildcardPublicTests|NativeTextRawReadPolicyPublicTests|NativeTextRequestRefusalPublicTests|NativeTextRowOwnershipPublicTests|GraphVertexPublicWholeFlowTests|QueueLeaseFencingRf3Tests|PartitionMovementFencedAbortRf3Tests|TenantDedicatedOwnerRf3Tests|PostgresNativeSessionRf3Tests|BlobRowCreatorPublicTests|BlobRejectedContentPublicTests|BlobListRowPolicyPublicTests|NativeTextNormalizationPublicTests|NativeTextDistinctTermsPublicTests)/*").ConfigureAwait(false);
        await Assert.That(root.GetProperty(SuccessorRejectedProperty).GetInt32()).IsEqualTo(ExpectedRefusals);
    }

    internal static async Task AssertAsync(JsonElement root)
    {
        await NativeTUnitSelectionTests.AssertLocalSelectorAsync(root.GetProperty(SelectedProperty), Filter).ConfigureAwait(false);
        await Assert.That(root.GetProperty(RejectedProperty).GetInt32()).IsEqualTo(ExpectedRefusals);
    }
}
