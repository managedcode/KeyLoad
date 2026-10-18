namespace KeyLoad.UnitTests.Features.TestInfrastructure;

internal static class NativePublicCohortSelectionProgram
{
    internal const string FinalSource = """
        const finalPublicCohortFilter = "/*/*/(BlobActiveUploadSlotPublicTests|BlobBoundedRetirementPublicTests|BlobCompleteCasPublicTests|BlobEmptySlotPublicTests|BlobListRowPolicyPublicTests|BlobObjectKeyLifetimePublicTests|BlobPartialReadBoundaryPublicTests|BlobReadPolicyPublicTests|BlobRejectedContentPublicTests|BlobReservationQuotaPublicTests|BlobRowCreatorPublicTests|BlobScopedUploadIdentityPublicTests|BlobSupersessionPublicTests|DeclarativeCompositionReverseQuotaClusterTests|GraphVertexPublicWholeFlowTests|NativeTextDistinctTermsPublicTests|NativeTextNestedWildcardPublicTests|NativeTextNonStringTransitionsPublicTests|NativeTextNormalizationPublicTests|NativeTextProtectedPublicColdTests|NativeTextRawReadPolicyPublicTests|NativeTextRequestRefusalPublicTests|NativeTextRowOwnershipPublicTests|PostgresDecimalClusterWholeFlowTests|PostgresErrorReplyReceiverClusterTests|PostgresInt16ClusterWholeFlowTests|PostgresNativeSessionClusterTests|PostgresPortalOwnershipClusterTests|PostgresPositiveExecuteClusterTests|ProjectionPublicColdTests|QueueLeaseFencingClusterTests|QueueLifecyclePublicClusterTests|SqlFetchCountClusterWholeFlowTests|SqlNullDistinctClusterWholeFlowTests|SqlNullOrderingClusterWholeFlowTests|SqlOffsetClusterWholeFlowTests|SubscriptionPhasePublicColdTests|TypedEndpointJoinClusterTests)/*";
        const finalPublicCohortArgs = Object.freeze(publicCohortArgs.map(value =>
          value.startsWith('--KeyLoadTests:Filter=') ? '--KeyLoadTests:Filter=' + finalPublicCohortFilter : value));
        const finalPublicCohortInputBytes = JSON.stringify(finalPublicCohortArgs);
        const finalPublicCohort = nativeSelection(finalPublicCohortArgs, publicCohortEnvironment);
        let finalPublicCohortUnsupportedCount = 0;
        for (const badFilter of [
          '/*/*/PostgresInt16ClusterWholeFlowTests/*',
          finalPublicCohortFilter.replace('BlobActiveUploadSlotPublicTests|', ''),
          finalPublicCohortFilter.replace('BlobActiveUploadSlotPublicTests', 'BlobActiveUploadSlotPublicTests*'),
          finalPublicCohortFilter.replace('BlobActiveUploadSlotPublicTests|BlobBoundedRetirementPublicTests',
            'BlobBoundedRetirementPublicTests|BlobActiveUploadSlotPublicTests'),
          finalPublicCohortFilter.replace(')/*', '|OtherCase)/*'),
          finalPublicCohortFilter + ' ']) {
          try { nativeSelection([localArgs[0], '--KeyLoadTests:Filter=' + badFilter, localArgs[2]], {}); }
          catch (failure) {
            if (failure.message !== 'Invalid native local CLUSTER image selection.') throw failure;
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
        const nextPublicCohortFilter = "/*/*/(BlobActiveUploadSlotPublicTests|BlobBoundedRetirementPublicTests|BlobCompleteCasPublicTests|BlobEmptySlotPublicTests|BlobListRowPolicyPublicTests|BlobObjectKeyLifetimePublicTests|BlobPartialReadBoundaryPublicTests|BlobReadPolicyPublicTests|BlobRejectedContentPublicTests|BlobReservationQuotaPublicTests|BlobRowCreatorPublicTests|BlobScopedUploadIdentityPublicTests|BlobSupersessionPublicTests|DeclarativeCompositionReverseQuotaClusterTests|GraphVertexPublicWholeFlowTests|NativeTextDistinctTermsPublicTests|NativeTextNestedWildcardPublicTests|NativeTextNonStringTransitionsPublicTests|NativeTextNormalizationPublicTests|NativeTextProtectedPublicColdTests|NativeTextRawReadPolicyPublicTests|NativeTextRequestRefusalPublicTests|NativeTextRowOwnershipPublicTests|PostgresDecimalClusterWholeFlowTests|PostgresErrorReplyReceiverClusterTests|PostgresNativeSessionClusterTests|PostgresPortalOwnershipClusterTests|PostgresPositiveExecuteClusterTests|ProjectionPublicColdTests|QueueLeaseFencingClusterTests|QueueLifecyclePublicClusterTests|SqlFetchCountClusterWholeFlowTests|SqlNullDistinctClusterWholeFlowTests|SqlNullOrderingClusterWholeFlowTests|SqlOffsetClusterWholeFlowTests|SubscriptionPhasePublicColdTests|TypedEndpointJoinClusterTests)/*";
        const nextPublicCohortArgs = Object.freeze(publicCohortArgs.map(value =>
          value.startsWith('--KeyLoadTests:Filter=') ? '--KeyLoadTests:Filter=' + nextPublicCohortFilter : value));
        const nextPublicCohortInputBytes = JSON.stringify(nextPublicCohortArgs);
        const nextPublicCohort = nativeSelection(nextPublicCohortArgs, publicCohortEnvironment);
        let nextPublicCohortUnsupportedCount = 0;
        for (const badFilter of [
          '/*/*/BlobEmptySlotPublicTests/*',
          nextPublicCohortFilter.replace('BlobActiveUploadSlotPublicTests|', ''),
          nextPublicCohortFilter.replace('BlobActiveUploadSlotPublicTests', 'BlobActiveUploadSlotPublicTests*'),
          nextPublicCohortFilter.replace('BlobActiveUploadSlotPublicTests|BlobBoundedRetirementPublicTests',
            'BlobBoundedRetirementPublicTests|BlobActiveUploadSlotPublicTests'),
          nextPublicCohortFilter.replace(')/*', '|OtherCase)/*'),
          nextPublicCohortFilter + ' ']) {
          try { nativeSelection([localArgs[0], '--KeyLoadTests:Filter=' + badFilter, localArgs[2]], {}); }
          catch (failure) {
            if (failure.message !== 'Invalid native local CLUSTER image selection.') throw failure;
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
        const publicCohortFilter = "/*/*/(BlobBoundedRetirementPublicTests|BlobListRowPolicyPublicTests|BlobPartialReadBoundaryPublicTests|BlobReadPolicyPublicTests|BlobRejectedContentPublicTests|BlobReservationQuotaPublicTests|BlobRowCreatorPublicTests|BlobSupersessionPublicTests|DeclarativeCompositionReverseQuotaClusterTests|GraphVertexPublicWholeFlowTests|NativeTextDistinctTermsPublicTests|NativeTextNestedWildcardPublicTests|NativeTextNormalizationPublicTests|NativeTextProtectedPublicColdTests|NativeTextRawReadPolicyPublicTests|NativeTextRequestRefusalPublicTests|NativeTextRowOwnershipPublicTests|PostgresNativeSessionClusterTests|PostgresPortalOwnershipClusterTests|PostgresPositiveExecuteClusterTests|ProjectionPublicColdTests|QueueLeaseFencingClusterTests|QueueLifecyclePublicClusterTests|SqlFetchCountClusterWholeFlowTests|SqlNullOrderingClusterWholeFlowTests|SqlOffsetClusterWholeFlowTests|SubscriptionPhasePublicColdTests|TypedEndpointJoinClusterTests)/*";
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
          '/*/*/SqlOffsetClusterWholeFlowTests/*',
          publicCohortFilter.replace('BlobBoundedRetirementPublicTests|', ''),
          publicCohortFilter.replace('BlobBoundedRetirementPublicTests', 'BlobBoundedRetirementPublicTests*'),
          publicCohortFilter.replace('BlobBoundedRetirementPublicTests|BlobListRowPolicyPublicTests',
            'BlobListRowPolicyPublicTests|BlobBoundedRetirementPublicTests'),
          publicCohortFilter.replace(')/*', '|OtherCase)/*'),
          publicCohortFilter + ' ']) {
          try { nativeSelection([localArgs[0], '--KeyLoadTests:Filter=' + badFilter, localArgs[2]], {}); }
          catch (failure) {
            if (failure.message !== 'Invalid native local CLUSTER image selection.') throw failure;
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
