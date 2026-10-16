namespace KeyLoad.UnitTests.Features.TestInfrastructure;

internal static class NativeCurrentModelSelectionProgram
{
    internal const string Source = """
            const currentModelFilters = Object.freeze([
              "/*/*/GraphSearchPathPolicyRf3Tests/*",
              "/*/*/SampleChunkCorrectionPredicateRf3Tests/*",
              "/*/*/(SqlParserAdmissionRf3WholeFlowTests|GraphSearchPathPolicyRf3Tests)/*",
              "/*/*/(SqlParserAdmissionRf3WholeFlowTests|GraphSearchPathPolicyRf3Tests|SampleChunkCorrectionPredicateRf3Tests)/*",
              "/*/*/(GraphSearchPathPolicyRf3Tests|SampleChunkCorrectionPredicateRf3Tests)/*",
              "/*/*/(NativeDatabaseMigrationRf3Tests|SqlParserAdmissionRf3WholeFlowTests|GraphSearchPathPolicyRf3Tests|RequestCqrsPhaseFaultTests|RequestCqrsAuthorityFaultTests|RequestCqrsRf3FollowerRestartTests|SagaDueRf3Tests|SagaTimeoutRf3Tests|SampleChunkCorrectionPredicateRf3Tests|ConnectionRf3AuthorizationTests|DocumentSessionReadRf3Tests|MetadataBootstrapRf3Tests|PartitionMovementPublicParentFlowTests|PhysicalOwnerRegistrationRf3Tests|ProtectedDocumentRf3Tests|QueryContextSessionRf3Tests|StrongReadSessionAbsenceRf3Tests)/*",
              "/*/*/AspireContainerFaultCommandTests/WrongStaleAndForeignIdsRefuseWithoutEffectsBeforeHealthySdkMcpRestart",
              "/*/*/AggregateReplayRf3LeaderLossTests/AcEvent008AcceptedRestartCancellationResumesSameReplacementAndPreservesReplay",
              "/*/*/ControlledPartitionMovementNativeRetireTests/OriginalNativeRetireCallerCancellationAndHostStopRetainAuthorityThenResumeReplayMixedModelsAndTwoColdOwners",
              "/*/*/PartitionQueryPublicValidationTests/*",
              "/*/*/CrossTenantRf3WholeFlowTests/Kl015ForeignWritesScansAndIndexesAreDeniedAndAuthorizedReceiptRemainsStable",
              "/*/*/RemotePartitionQueryRf3Tests/AcPqueryRemote001To005DestinationDenialLiteralMergeReceiptReplayAndOwnedRestart",
              "/*/*/FunctionalDatabaseColdReopenTests/SingleInstanceColdReopensAllModelsAndReplaysOriginalPublicCommands",
              "/*/*/FunctionalDatabaseColdReopenTests/ThreeNodeColdReopensAllModelsAndReplaysOriginalPublicCommands",
              "/*/*/(PhysicalOwnerRegistrationRf3Tests|RemotePartitionQueryRf3Tests|RequestCqrsPhaseFaultTests|RequestCqrsRf3FollowerRestartTests)/*",
              "/*/*/ControlledBlobLifetimeReadRf3Tests/HeldNativeRepliesRequireFreshSourceAuthorityAndSettleBeforeTwoColdBlobReadWaves",
              "/*/*/(PartitionMovementBlobWireNativeTests|NativeBlobWholeParentQualificationCases)/*"
            ]);
            const currentModelArgs = Object.freeze(currentModelFilters.map(filter => Object.freeze(
              publicCohortArgs.map(value => value.startsWith('--KeyLoadTests:Filter=')
                ? '--KeyLoadTests:Filter=' + filter : value))));
            const currentModelInputBytes = JSON.stringify(currentModelArgs);
            const currentModels = currentModelArgs.map(args => nativeSelection(args, publicCohortEnvironment));
            for (const badFilter of [
              currentModelFilters[16] + ' ',
              currentModelFilters[16] + '*',
              '/*/*/(NativeBlobWholeParentQualificationCases|PartitionMovementBlobWireNativeTests)/*',
              '/*/*/PartitionMovementBlobWireNativeTests/*',
              '/*/*/NativeBlobWholeParentQualificationCases/*',
              currentModelFilters[16].replace('PartitionMovementBlobWireNativeTests|', 'PartitionMovementBlobWireNativeTests*|'),
              currentModelFilters[16].replace(')/*', ')/OtherMethod'),
              currentModelFilters[16].replace(')/*', '|OtherCase)/*'),
              currentModelFilters[15] + ' ',
              currentModelFilters[15] + '*',
              currentModelFilters[15].replace('ControlledBlobLifetimeReadRf3Tests/', 'ControlledBlobLifetimeReadRf3Tests*/'),
              '/*/*/ControlledBlobLifetimeReadRf3Tests/*',
              '/*/*/ControlledBlobLifetimeReadRf3Tests/OtherMethod',
              '/*/*/HeldNativeRepliesRequireFreshSourceAuthorityAndSettleBeforeTwoColdBlobReadWaves/ControlledBlobLifetimeReadRf3Tests',
              currentModelFilters[14] + ' ',
              currentModelFilters[14] + '*',
              currentModelFilters[14].replace('PhysicalOwnerRegistrationRf3Tests|', 'PhysicalOwnerRegistrationRf3Tests*|'),
              currentModelFilters[14].replace('PhysicalOwnerRegistrationRf3Tests|RemotePartitionQueryRf3Tests',
                'RemotePartitionQueryRf3Tests|PhysicalOwnerRegistrationRf3Tests'),
              currentModelFilters[14].replace('PhysicalOwnerRegistrationRf3Tests|', ''),
              currentModelFilters[14].replace(')/*', '|OtherCase)/*'),
              currentModelFilters[14].replace(')/*', ')/OtherMethod'),
              '/*/*/PhysicalOwnerRegistrationRf3Tests/*',
              '/*/*/RequestCqrsPhaseFaultTests/*',
              '/*/*/RequestCqrsRf3FollowerRestartTests/*',
              '/*/*/FunctionalDatabaseColdReopenTests/*',
              '/*/*/FunctionalDatabaseColdReopenTests*/SingleInstanceColdReopensAllModelsAndReplaysOriginalPublicCommands',
              currentModelFilters[12] + '*',
              currentModelFilters[12] + ' ',
              '/*/*/FunctionalDatabaseColdReopenTests*/ThreeNodeColdReopensAllModelsAndReplaysOriginalPublicCommands',
              currentModelFilters[13] + '*',
              currentModelFilters[13] + ' ',
              '/*/*/FunctionalDatabaseColdReopenTests/OtherMethod',
              '/*/*/(FunctionalDatabaseColdReopenTests|FunctionalDatabaseGateTests)/*',
              '/*/*/RemotePartitionQueryRf3Tests/*',
              '/*/*/RemotePartitionQueryRf3Tests*/AcPqueryRemote001To005DestinationDenialLiteralMergeReceiptReplayAndOwnedRestart',
              currentModelFilters[11] + '*',
              currentModelFilters[11] + ' ',
              '/*/*/RemotePartitionQueryRf3Tests/OtherMethod',
              '/*/*/(RemotePartitionQueryRf3Tests|DistributedQueryAuthorityRf3Tests)/*',
              '/*/*/CrossTenantRf3WholeFlowTests/*',
              '/*/*/CrossTenantRf3WholeFlowTests*/Kl015ForeignWritesScansAndIndexesAreDeniedAndAuthorizedReceiptRemainsStable',
              currentModelFilters[10] + '*',
              currentModelFilters[10] + ' ',
              '/*/*/(CrossTenantRf3WholeFlowTests|PartitionQueryPublicValidationTests)/*',
              '/*/*/PartitionQueryPublicValidationTests*/*',
              currentModelFilters[9] + ' ',
              '/*/*/PartitionQueryPublicValidationTests/AcPquery001InvalidVersionAndLeafOverflowAreValidationWithHealthyFollowup',
              '/*/*/PartitionQueryPublicValidationTests/AcPquery005OversizedRequestAndCancelledSdkAndMcpCallsHaveHealthyFollowups',
              '/*/*/(McpPersistedAuthorizationTests|PartitionQueryPublicValidationTests)/*',
              '/*/*/ControlledPartitionMovementNativeRetireTests/*',
              currentModelFilters[8] + ' ',
              currentModelFilters[8].replace('ControlledPartitionMovementNativeRetireTests/', 'ControlledPartitionMovementNativeRetireTests*/'),
              currentModelFilters[8] + '*',
              '/*/*/ControlledPartitionMovementNativeRetireTests/OtherMethod',
              '/*/*/(ControlledPartitionMovementNativeRetireTests|NativeDatabaseMigrationRf3Tests)/*',
              '/*/*/AspireContainerFaultCommandTests/*',
              currentModelFilters[6] + ' ',
              currentModelFilters[6].replace('AspireContainerFaultCommandTests/', 'AspireContainerFaultCommandTests*/'),
              currentModelFilters[6] + '*',
              '/*/*/AggregateReplayRf3LeaderLossTests/*',
              currentModelFilters[7] + ' ',
              currentModelFilters[7].replace('AggregateReplayRf3LeaderLossTests/', 'AggregateReplayRf3LeaderLossTests*/'),
              currentModelFilters[7] + '*',
              '/*/*/AggregateReplayRf3LeaderLossTests/AcEvent008SnapshotTailCommandRecoversAcrossLeaderLossAndContainerRestart',
              '/*/*/(AspireContainerFaultCommandTests|AggregateReplayRf3LeaderLossTests)/*',
              '/*/*/GraphSearchPathPolicyRf3Tests*/*',
              '/*/*/SampleChunkCorrectionPredicateRf3Tests*/*',
              '/*/*/(GraphSearchPathPolicyRf3Tests|SqlParserAdmissionRf3WholeFlowTests)/*',
              '/*/*/(SampleChunkCorrectionPredicateRf3Tests|SqlParserAdmissionRf3WholeFlowTests|GraphSearchPathPolicyRf3Tests)/*',
              currentModelFilters[2] + ' ',
              currentModelFilters[3] + ' ',
              currentModelFilters[2].replace(')/*', '|OtherCase)/*'),
              currentModelFilters[3].replace('GraphSearchPathPolicyRf3Tests|', ''),
              '/*/*/(SampleChunkCorrectionPredicateRf3Tests|GraphSearchPathPolicyRf3Tests)/*',
              currentModelFilters[4] + ' ',
              currentModelFilters[4].replace(')/*', '|OtherCase)/*'),
              currentModelFilters[5] + ' ',
              currentModelFilters[5].replace(')/*', '|OtherCase)/*'),
              currentModelFilters[5].replace('NativeDatabaseMigrationRf3Tests|SqlParserAdmissionRf3WholeFlowTests',
                'SqlParserAdmissionRf3WholeFlowTests|NativeDatabaseMigrationRf3Tests'),
              currentModelFilters[5].replace('NativeDatabaseMigrationRf3Tests|', ''),
              currentModelFilters[5].replace('StrongReadSessionAbsenceRf3Tests', 'StrongReadSessionAbsenceRf3Tests*')]) {
              let rejected = false;
              try { nativeSelection([localArgs[0], '--KeyLoadTests:Filter=' + badFilter, localArgs[2]], {}); }
              catch (failure) {
                if (failure.message !== 'Invalid native local RF3 image selection.') throw failure;
                rejected = true;
              }
              if (!rejected) throw new Error('Unsupported current model cohort selector was accepted.');
            }
            for (const addedIndex of [15, 16]) {
              const currentModelHealthy = nativeSelection(currentModelArgs[addedIndex], publicCohortEnvironment);
              if (JSON.stringify(currentModelHealthy) !== JSON.stringify(currentModels[addedIndex])) {
                throw new Error('Exact current model selection changed after refused selectors.');
              }
            }
            if (JSON.stringify(currentModelArgs) !== currentModelInputBytes
              || JSON.stringify(publicCohortEnvironment) !== publicCohortEnvironmentBytes
              || publicSelectionPathExists(publicCohortResultsPath)) {
              throw new Error('Current model selection mutated inputs or created output resources.');
            }
        """;
}
