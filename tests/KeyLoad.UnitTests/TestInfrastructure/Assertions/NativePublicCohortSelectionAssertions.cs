using System.Text.Json;

namespace KeyLoad.UnitTests.Features.TestInfrastructure;

internal static class NativePublicCohortSelectionAssertions
{
    private const int ExpectedRefusals = 6;
    private const string SelectedProperty = "publicCohort";
    private const string RejectedProperty = "publicCohortUnsupportedCount";
    private const string NoEffectsProperty = "publicCohortNoEffects";
    private const string ArgumentsProperty = "args";
    private const string EnvironmentProperty = "environment";
    private const string ReportArgument = "--report-trx";
    private const string TimeoutArgument = "--timeout";
    private const string ExpectedTimeout = "60m";
    private const string SentinelProperty = "KEYLOAD_SELECTION_SENTINEL";
    private const string OriginalSentinel = "original";
    private const string Filter = "/*/*/(BlobArchiveActivePublicTests|BlobBoundedRetirementPublicTests|BlobListRowPolicyPublicTests|BlobPartialReadBoundaryPublicTests|BlobPublicCancellationTests|BlobReadPolicyPublicTests|BlobRejectedContentPublicTests|BlobReservationQuotaPublicTests|BlobRestoredReclaimPublicTests|BlobRowCreatorPublicTests|BlobSupersessionPublicTests|CurrentOwnershipTokenAfterMovementRf3Tests|DeclarativeCompositionReverseQuotaRf3Tests|GraphVertexPublicWholeFlowTests|NativeTextDistinctTermsPublicTests|NativeTextNestedWildcardPublicTests|NativeTextNormalizationPublicTests|NativeTextProtectedPublicColdTests|NativeTextRawReadPolicyPublicTests|NativeTextRequestRefusalPublicTests|NativeTextRowOwnershipPublicTests|NativeTextSelectedSchemaPublicTests|PartitionMovementFencedAbortRf3Tests|PhysicalShardPartitionMovementRf3Tests|PostgresNativeSessionRf3Tests|PostgresPortalOwnershipRf3Tests|PostgresPositiveExecuteRf3Tests|ProjectionPublicColdTests|QueueLeaseFencingRf3Tests|QueueLifecyclePublicRf3Tests|SqlFetchCountRf3WholeFlowTests|SqlNullOrderingRf3WholeFlowTests|SqlOffsetRf3WholeFlowTests|SubscriptionPhasePublicColdTests|TenantCapacityPlacementRf3Tests|TenantDedicatedOwnerRf3Tests|TenantPackingActivationRf3Tests|TypedEndpointJoinRf3Tests)/*";

    private const string FinalSelectedProperty = "finalPublicCohort";
    private const string FinalRejectedProperty = "finalPublicCohortUnsupportedCount";
    private const string FinalNoEffectsProperty = "finalPublicCohortNoEffects";
    private const string FinalFilter = "/*/*/(BlobActiveUploadSlotPublicTests|BlobArchiveActivePublicTests|BlobBoundedRetirementPublicTests|BlobCompleteCasPublicTests|BlobEmptySlotPublicTests|BlobListRowPolicyPublicTests|BlobObjectKeyLifetimePublicTests|BlobPartialReadBoundaryPublicTests|BlobPublicCancellationTests|BlobReadPolicyPublicTests|BlobRejectedContentPublicTests|BlobReservationQuotaPublicTests|BlobRestoredReclaimPublicTests|BlobRowCreatorPublicTests|BlobScopedUploadIdentityPublicTests|BlobSupersessionPublicTests|CurrentOwnershipTokenAfterMovementRf3Tests|DeclarativeCompositionReverseQuotaRf3Tests|GraphVertexPublicWholeFlowTests|NativeTextDistinctTermsPublicTests|NativeTextEmptyMaintainedPublicTests|NativeTextFreshSchemaGenerationPublicTests|NativeTextNestedWildcardPublicTests|NativeTextNonStringTransitionsPublicTests|NativeTextNormalizationPublicTests|NativeTextPartitionGenerationPublicTests|NativeTextProtectedPublicColdTests|NativeTextRawReadPolicyPublicTests|NativeTextRequestRefusalPublicTests|NativeTextRowOwnershipPublicTests|NativeTextSelectedSchemaPublicTests|PartitionMovementFencedAbortRf3Tests|PhysicalShardPartitionMovementRf3Tests|PostgresDecimalRf3WholeFlowTests|PostgresErrorReplyReceiverRf3Tests|PostgresInt16Rf3WholeFlowTests|PostgresNativeSessionRf3Tests|PostgresPortalOwnershipRf3Tests|PostgresPositiveExecuteRf3Tests|ProjectionPublicColdTests|QueueLeaseFencingRf3Tests|QueueLifecyclePublicRf3Tests|SqlFetchCountRf3WholeFlowTests|SqlNullDistinctRf3WholeFlowTests|SqlNullOrderingRf3WholeFlowTests|SqlOffsetRf3WholeFlowTests|SubscriptionPhasePublicColdTests|TenantCapacityPlacementRf3Tests|TenantDedicatedOwnerRf3Tests|TenantPackingActivationRf3Tests|TypedEndpointJoinRf3Tests)/*";

    private static async Task AssertFinalAsync(JsonElement root)
    {
        await NativeTUnitSelectionTests.AssertLocalSelectorAsync(root.GetProperty(FinalSelectedProperty), FinalFilter)
            .ConfigureAwait(false);
        await Assert.That(root.GetProperty(FinalRejectedProperty).GetInt32()).IsEqualTo(ExpectedRefusals);
        await Assert.That(root.GetProperty(FinalNoEffectsProperty).GetBoolean()).IsTrue();
        var arguments = root.GetProperty(FinalSelectedProperty).GetProperty(ArgumentsProperty).EnumerateArray()
            .Select(value => value.GetString()).ToArray();
        await Assert.That(arguments).Contains(ReportArgument);
        await Assert.That(arguments[Array.IndexOf(arguments, TimeoutArgument) + 1]).IsEqualTo(ExpectedTimeout);
        await Assert.That(root.GetProperty(FinalSelectedProperty).GetProperty(EnvironmentProperty)
            .GetProperty(SentinelProperty).GetString()).IsEqualTo(OriginalSentinel);
    }

    private const string NextSelectedProperty = "nextPublicCohort";
    private const string NextRejectedProperty = "nextPublicCohortUnsupportedCount";
    private const string NextNoEffectsProperty = "nextPublicCohortNoEffects";
    private const string NextFilter = "/*/*/(BlobActiveUploadSlotPublicTests|BlobArchiveActivePublicTests|BlobBoundedRetirementPublicTests|BlobCompleteCasPublicTests|BlobEmptySlotPublicTests|BlobListRowPolicyPublicTests|BlobObjectKeyLifetimePublicTests|BlobPartialReadBoundaryPublicTests|BlobPublicCancellationTests|BlobReadPolicyPublicTests|BlobRejectedContentPublicTests|BlobReservationQuotaPublicTests|BlobRestoredReclaimPublicTests|BlobRowCreatorPublicTests|BlobScopedUploadIdentityPublicTests|BlobSupersessionPublicTests|CurrentOwnershipTokenAfterMovementRf3Tests|DeclarativeCompositionReverseQuotaRf3Tests|GraphVertexPublicWholeFlowTests|NativeTextDistinctTermsPublicTests|NativeTextEmptyMaintainedPublicTests|NativeTextFreshSchemaGenerationPublicTests|NativeTextNestedWildcardPublicTests|NativeTextNonStringTransitionsPublicTests|NativeTextNormalizationPublicTests|NativeTextPartitionGenerationPublicTests|NativeTextProtectedPublicColdTests|NativeTextRawReadPolicyPublicTests|NativeTextRequestRefusalPublicTests|NativeTextRowOwnershipPublicTests|NativeTextSelectedSchemaPublicTests|PartitionMovementFencedAbortRf3Tests|PhysicalShardPartitionMovementRf3Tests|PostgresDecimalRf3WholeFlowTests|PostgresErrorReplyReceiverRf3Tests|PostgresNativeSessionRf3Tests|PostgresPortalOwnershipRf3Tests|PostgresPositiveExecuteRf3Tests|ProjectionPublicColdTests|QueueLeaseFencingRf3Tests|QueueLifecyclePublicRf3Tests|SqlFetchCountRf3WholeFlowTests|SqlNullDistinctRf3WholeFlowTests|SqlNullOrderingRf3WholeFlowTests|SqlOffsetRf3WholeFlowTests|SubscriptionPhasePublicColdTests|TenantCapacityPlacementRf3Tests|TenantDedicatedOwnerRf3Tests|TenantPackingActivationRf3Tests|TypedEndpointJoinRf3Tests)/*";

    private static async Task AssertSuccessorAsync(JsonElement root)
    {
        await NativeTUnitSelectionTests.AssertLocalSelectorAsync(root.GetProperty(NextSelectedProperty), NextFilter)
            .ConfigureAwait(false);
        await Assert.That(root.GetProperty(NextRejectedProperty).GetInt32()).IsEqualTo(ExpectedRefusals);
        await Assert.That(root.GetProperty(NextNoEffectsProperty).GetBoolean()).IsTrue();
        var arguments = root.GetProperty(NextSelectedProperty).GetProperty(ArgumentsProperty).EnumerateArray()
            .Select(value => value.GetString()).ToArray();
        await Assert.That(arguments).Contains(ReportArgument);
        await Assert.That(arguments[Array.IndexOf(arguments, TimeoutArgument) + 1]).IsEqualTo(ExpectedTimeout);
        await Assert.That(root.GetProperty(NextSelectedProperty).GetProperty(EnvironmentProperty)
            .GetProperty(SentinelProperty).GetString()).IsEqualTo(OriginalSentinel);
    }

    internal static async Task AssertAsync(JsonElement root)
    {
        await AssertFinalAsync(root).ConfigureAwait(false);
        await AssertSuccessorAsync(root).ConfigureAwait(false);
        await NativeTUnitSelectionTests.AssertLocalSelectorAsync(root.GetProperty(SelectedProperty), Filter)
            .ConfigureAwait(false);
        await Assert.That(root.GetProperty(RejectedProperty).GetInt32()).IsEqualTo(ExpectedRefusals);
        await Assert.That(root.GetProperty(NoEffectsProperty).GetBoolean()).IsTrue();
        var arguments = root.GetProperty(SelectedProperty).GetProperty(ArgumentsProperty).EnumerateArray()
            .Select(value => value.GetString()).ToArray();
        await Assert.That(arguments).Contains(ReportArgument);
        await Assert.That(arguments[Array.IndexOf(arguments, TimeoutArgument) + 1]).IsEqualTo(ExpectedTimeout);
        await Assert.That(root.GetProperty(SelectedProperty).GetProperty(EnvironmentProperty)
            .GetProperty(SentinelProperty).GetString()).IsEqualTo(OriginalSentinel);
    }
}
