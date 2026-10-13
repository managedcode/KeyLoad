namespace KeyLoad.UnitTests.Features.TestInfrastructure;

internal static class NativePublicCohortSelectionProgram
{
    internal const string FinalSource = """
        const finalPublicCohortFilter = "/*/*/(BlobActiveUploadSlotPublicTests|BlobArchiveActivePublicTests|BlobBoundedRetirementPublicTests|BlobCompleteCasPublicTests|BlobEmptySlotPublicTests|BlobListRowPolicyPublicTests|BlobObjectKeyLifetimePublicTests|BlobPartialReadBoundaryPublicTests|BlobPublicCancellationTests|BlobReadPolicyPublicTests|BlobRejectedContentPublicTests|BlobReservationQuotaPublicTests|BlobRestoredReclaimPublicTests|BlobRowCreatorPublicTests|BlobScopedUploadIdentityPublicTests|BlobSupersessionPublicTests|CurrentOwnershipTokenAfterMovementRf3Tests|DeclarativeCompositionReverseQuotaRf3Tests|GraphVertexPublicWholeFlowTests|NativeTextDistinctTermsPublicTests|NativeTextEmptyMaintainedPublicTests|NativeTextFreshSchemaGenerationPublicTests|NativeTextNestedWildcardPublicTests|NativeTextNonStringTransitionsPublicTests|NativeTextNormalizationPublicTests|NativeTextPartitionGenerationPublicTests|NativeTextProtectedPublicColdTests|NativeTextRawReadPolicyPublicTests|NativeTextRequestRefusalPublicTests|NativeTextRowOwnershipPublicTests|NativeTextSelectedSchemaPublicTests|PartitionMovementFencedAbortRf3Tests|PhysicalShardPartitionMovementRf3Tests|PostgresDecimalRf3WholeFlowTests|PostgresErrorReplyReceiverRf3Tests|PostgresInt16Rf3WholeFlowTests|PostgresNativeSessionRf3Tests|PostgresPortalOwnershipRf3Tests|PostgresPositiveExecuteRf3Tests|ProjectionPublicColdTests|QueueLeaseFencingRf3Tests|QueueLifecyclePublicRf3Tests|SqlFetchCountRf3WholeFlowTests|SqlNullDistinctRf3WholeFlowTests|SqlNullOrderingRf3WholeFlowTests|SqlOffsetRf3WholeFlowTests|SubscriptionPhasePublicColdTests|TenantCapacityPlacementRf3Tests|TenantDedicatedOwnerRf3Tests|TenantPackingActivationRf3Tests|TypedEndpointJoinRf3Tests)/*";
        const finalPublicCohortArgs = Object.freeze(publicCohortArgs.map(value =>
          value.startsWith('--KeyLoadTests:Filter=') ? '--KeyLoadTests:Filter=' + finalPublicCohortFilter : value));
        const finalPublicCohortInputBytes = JSON.stringify(finalPublicCohortArgs);
        const finalPublicCohort = nativeSelection(finalPublicCohortArgs, publicCohortEnvironment);
        let finalPublicCohortUnsupportedCount = 0;
        for (const badFilter of [
          '/*/*/PostgresInt16Rf3WholeFlowTests/*',
          finalPublicCohortFilter.replace('BlobActiveUploadSlotPublicTests|', ''),
          finalPublicCohortFilter.replace('BlobActiveUploadSlotPublicTests', 'BlobActiveUploadSlotPublicTests*'),
          finalPublicCohortFilter.replace('BlobActiveUploadSlotPublicTests|BlobArchiveActivePublicTests',
            'BlobArchiveActivePublicTests|BlobActiveUploadSlotPublicTests'),
          finalPublicCohortFilter.replace(')/*', '|OtherCase)/*'),
          finalPublicCohortFilter + ' ']) {
          try { nativeSelection([localArgs[0], '--KeyLoadTests:Filter=' + badFilter, localArgs[2]], {}); }
          catch (failure) {
            if (failure.message !== 'Invalid native local RF3 image selection.') throw failure;
            finalPublicCohortUnsupportedCount++; continue;
          }
          throw new Error('Unsupported final public cohort filter was accepted.');
        }
        if (JSON.stringify(finalPublicCohortArgs) !== finalPublicCohortInputBytes
          || JSON.stringify(publicCohortEnvironment) !== publicCohortEnvironmentBytes
          || publicSelectionPathExists(publicCohortResultsPath)) {
          throw new Error('Native successor selection mutated inputs or created output resources.');
        }
        const finalPublicCohortNoEffects = true;
        """;

    internal const string SuccessorSource = """
        const nextPublicCohortFilter = "/*/*/(BlobActiveUploadSlotPublicTests|BlobArchiveActivePublicTests|BlobBoundedRetirementPublicTests|BlobCompleteCasPublicTests|BlobEmptySlotPublicTests|BlobListRowPolicyPublicTests|BlobObjectKeyLifetimePublicTests|BlobPartialReadBoundaryPublicTests|BlobPublicCancellationTests|BlobReadPolicyPublicTests|BlobRejectedContentPublicTests|BlobReservationQuotaPublicTests|BlobRestoredReclaimPublicTests|BlobRowCreatorPublicTests|BlobScopedUploadIdentityPublicTests|BlobSupersessionPublicTests|CurrentOwnershipTokenAfterMovementRf3Tests|DeclarativeCompositionReverseQuotaRf3Tests|GraphVertexPublicWholeFlowTests|NativeTextDistinctTermsPublicTests|NativeTextEmptyMaintainedPublicTests|NativeTextFreshSchemaGenerationPublicTests|NativeTextNestedWildcardPublicTests|NativeTextNonStringTransitionsPublicTests|NativeTextNormalizationPublicTests|NativeTextPartitionGenerationPublicTests|NativeTextProtectedPublicColdTests|NativeTextRawReadPolicyPublicTests|NativeTextRequestRefusalPublicTests|NativeTextRowOwnershipPublicTests|NativeTextSelectedSchemaPublicTests|PartitionMovementFencedAbortRf3Tests|PhysicalShardPartitionMovementRf3Tests|PostgresDecimalRf3WholeFlowTests|PostgresErrorReplyReceiverRf3Tests|PostgresNativeSessionRf3Tests|PostgresPortalOwnershipRf3Tests|PostgresPositiveExecuteRf3Tests|ProjectionPublicColdTests|QueueLeaseFencingRf3Tests|QueueLifecyclePublicRf3Tests|SqlFetchCountRf3WholeFlowTests|SqlNullDistinctRf3WholeFlowTests|SqlNullOrderingRf3WholeFlowTests|SqlOffsetRf3WholeFlowTests|SubscriptionPhasePublicColdTests|TenantCapacityPlacementRf3Tests|TenantDedicatedOwnerRf3Tests|TenantPackingActivationRf3Tests|TypedEndpointJoinRf3Tests)/*";
        const nextPublicCohortArgs = Object.freeze(publicCohortArgs.map(value =>
          value.startsWith('--KeyLoadTests:Filter=') ? '--KeyLoadTests:Filter=' + nextPublicCohortFilter : value));
        const nextPublicCohortInputBytes = JSON.stringify(nextPublicCohortArgs);
        const nextPublicCohort = nativeSelection(nextPublicCohortArgs, publicCohortEnvironment);
        let nextPublicCohortUnsupportedCount = 0;
        for (const badFilter of [
          '/*/*/BlobEmptySlotPublicTests/*',
          nextPublicCohortFilter.replace('BlobActiveUploadSlotPublicTests|', ''),
          nextPublicCohortFilter.replace('BlobActiveUploadSlotPublicTests', 'BlobActiveUploadSlotPublicTests*'),
          nextPublicCohortFilter.replace('BlobActiveUploadSlotPublicTests|BlobArchiveActivePublicTests',
            'BlobArchiveActivePublicTests|BlobActiveUploadSlotPublicTests'),
          nextPublicCohortFilter.replace(')/*', '|OtherCase)/*'),
          nextPublicCohortFilter + ' ']) {
          try { nativeSelection([localArgs[0], '--KeyLoadTests:Filter=' + badFilter, localArgs[2]], {}); }
          catch (failure) {
            if (failure.message !== 'Invalid native local RF3 image selection.') throw failure;
            nextPublicCohortUnsupportedCount++; continue;
          }
          throw new Error('Unsupported successor public cohort filter was accepted.');
        }
        if (JSON.stringify(nextPublicCohortArgs) !== nextPublicCohortInputBytes
          || JSON.stringify(publicCohortEnvironment) !== publicCohortEnvironmentBytes
          || publicSelectionPathExists(publicCohortResultsPath)) {
          throw new Error('Native successor selection mutated inputs or created output resources.');
        }
        const nextPublicCohortNoEffects = true;
        """;

    internal const string Source = """
        import { existsSync as publicSelectionPathExists } from 'node:fs';
        import { randomUUID as publicSelectionIdentity } from 'node:crypto';
        import { dirname as publicSelectionDirectory, resolve as publicSelectionPath } from 'node:path';
        const publicCohortFilter = "/*/*/(BlobArchiveActivePublicTests|BlobBoundedRetirementPublicTests|BlobListRowPolicyPublicTests|BlobPartialReadBoundaryPublicTests|BlobPublicCancellationTests|BlobReadPolicyPublicTests|BlobRejectedContentPublicTests|BlobReservationQuotaPublicTests|BlobRestoredReclaimPublicTests|BlobRowCreatorPublicTests|BlobSupersessionPublicTests|CurrentOwnershipTokenAfterMovementRf3Tests|DeclarativeCompositionReverseQuotaRf3Tests|GraphVertexPublicWholeFlowTests|NativeTextDistinctTermsPublicTests|NativeTextNestedWildcardPublicTests|NativeTextNormalizationPublicTests|NativeTextProtectedPublicColdTests|NativeTextRawReadPolicyPublicTests|NativeTextRequestRefusalPublicTests|NativeTextRowOwnershipPublicTests|NativeTextSelectedSchemaPublicTests|PartitionMovementFencedAbortRf3Tests|PhysicalShardPartitionMovementRf3Tests|PostgresNativeSessionRf3Tests|PostgresPortalOwnershipRf3Tests|PostgresPositiveExecuteRf3Tests|ProjectionPublicColdTests|QueueLeaseFencingRf3Tests|QueueLifecyclePublicRf3Tests|SqlFetchCountRf3WholeFlowTests|SqlNullOrderingRf3WholeFlowTests|SqlOffsetRf3WholeFlowTests|SubscriptionPhasePublicColdTests|TenantCapacityPlacementRf3Tests|TenantDedicatedOwnerRf3Tests|TenantPackingActivationRf3Tests|TypedEndpointJoinRf3Tests)/*";
        const publicCohortResults = 'TestResults/native-public-selection-' + publicSelectionIdentity();
        const publicCohortResultsPath = publicSelectionPath(publicSelectionDirectory(process.argv[1]),
          '../../..', publicCohortResults);
        const publicCohortArgs = Object.freeze([localArgs[0], '--KeyLoadTests:Filter=' + publicCohortFilter,
          localArgs[2], '--KeyLoadTests:Execution:MaximumParallelTests=50',
          '--KeyLoadTests:ReportTrx=true', '--KeyLoadTests:TimeoutMinutes=60',
          '--KeyLoadTests:ResultsDirectory=' + publicCohortResults]);
        const publicCohortEnvironment = Object.freeze({ KEYLOAD_SELECTION_SENTINEL: 'original' });
        const publicCohortInputBytes = JSON.stringify(publicCohortArgs);
        const publicCohortEnvironmentBytes = JSON.stringify(publicCohortEnvironment);
        if (publicSelectionPathExists(publicCohortResultsPath)) {
          throw new Error('The selection-only output probe already exists.');
        }
        const publicCohort = nativeSelection(publicCohortArgs, publicCohortEnvironment);
        let publicCohortUnsupportedCount = 0;
        for (const badFilter of [
          '/*/*/SqlOffsetRf3WholeFlowTests/*',
          publicCohortFilter.replace('BlobArchiveActivePublicTests|', ''),
          publicCohortFilter.replace('BlobArchiveActivePublicTests', 'BlobArchiveActivePublicTests*'),
          publicCohortFilter.replace('BlobArchiveActivePublicTests|BlobBoundedRetirementPublicTests',
            'BlobBoundedRetirementPublicTests|BlobArchiveActivePublicTests'),
          publicCohortFilter.replace(')/*', '|OtherCase)/*'),
          publicCohortFilter + ' ']) {
          try { nativeSelection([localArgs[0], '--KeyLoadTests:Filter=' + badFilter, localArgs[2]], {}); }
          catch (failure) {
            if (failure.message !== 'Invalid native local RF3 image selection.') throw failure;
            publicCohortUnsupportedCount++; continue;
          }
          throw new Error('Unsupported public cohort filter was accepted.');
        }
        if (JSON.stringify(publicCohortArgs) !== publicCohortInputBytes
          || JSON.stringify(publicCohortEnvironment) !== publicCohortEnvironmentBytes
          || publicSelectionPathExists(publicCohortResultsPath)) {
          throw new Error('Native selection mutated its original inputs or created output resources.');
        }
        const publicCohortNoEffects = true;
        """;
}
