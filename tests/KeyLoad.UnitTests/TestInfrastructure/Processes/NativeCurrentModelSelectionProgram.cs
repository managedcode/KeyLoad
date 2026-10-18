namespace KeyLoad.UnitTests.Features.TestInfrastructure;

internal static class NativeCurrentModelSelectionProgram
{
    internal const string Source = """
            const currentModelFilters = Object.freeze([
              "/*/*/GraphSearchPathPolicyClusterTests/*",
              "/*/*/(SqlParserAdmissionClusterWholeFlowTests|GraphSearchPathPolicyClusterTests)/*",
              "/*/*/(SqlParserAdmissionClusterWholeFlowTests|GraphSearchPathPolicyClusterTests|SagaDueClusterTests|SagaTimeoutClusterTests|DocumentSessionReadClusterTests|StrongReadSessionAbsenceClusterTests)/*",
              "/*/*/AspireContainerFaultCommandTests/WrongStaleAndForeignIdsRefuseWithoutEffectsBeforeHealthySdkMcpRestart",
              "/*/*/AggregateReplayClusterLeaderLossTests/AcEvent008AcceptedRestartCancellationResumesSameReplacementAndPreservesReplay",
              "/*/*/PartitionQueryPublicValidationTests/*",
              "/*/*/CrossTenantClusterWholeFlowTests/ForeignWritesScansAndIndexesAreDeniedAndAuthorizedReceiptRemainsStable",
              "/*/*/FunctionalDatabaseColdReopenTests/SingleInstanceColdReopensAllModelsAndReplaysOriginalPublicCommands",
              "/*/*/FunctionalDatabaseColdReopenTests/ThreeNodeColdReopensAllModelsAndReplaysOriginalPublicCommands",
              "/*/*/FunctionalFileOwnershipClusterTests/DistinctNativeFilesRetainAllModelsAcrossCompetingOwnersAndTwoColdReopens"
            ]);
            const currentModelArgs = Object.freeze(currentModelFilters.map(filter => Object.freeze(
              publicCohortArgs.map(value => value.startsWith('--KeyLoadTests:Filter=')
                ? '--KeyLoadTests:Filter=' + filter : value))));
            const currentModelInputBytes = JSON.stringify(currentModelArgs);
            const currentModels = currentModelArgs.map(args => nativeSelection(args, publicCohortEnvironment));
            for (const badFilter of [
              currentModelFilters[9] + ' ',
              currentModelFilters[9] + '*',
              currentModelFilters[9].replace('FunctionalFileOwnershipClusterTests/', 'FunctionalFileOwnershipClusterTests*/'),
              '/*/*/FunctionalFileOwnershipClusterTests/*',
              '/*/*/FunctionalFileOwnershipClusterTests/OtherMethod',
              '/*/*/DistinctNativeFilesRetainAllModelsAcrossCompetingOwnersAndTwoColdReopens/FunctionalFileOwnershipClusterTests',
              '/*/*/(FunctionalFileOwnershipClusterTests|FunctionalDatabaseColdReopenTests)/*',
              '/*/*/FunctionalDatabaseColdReopenTests/*',
              '/*/*/FunctionalDatabaseColdReopenTests*/SingleInstanceColdReopensAllModelsAndReplaysOriginalPublicCommands',
              currentModelFilters[7] + '*',
              currentModelFilters[7] + ' ',
              '/*/*/FunctionalDatabaseColdReopenTests*/ThreeNodeColdReopensAllModelsAndReplaysOriginalPublicCommands',
              currentModelFilters[8] + '*',
              currentModelFilters[8] + ' ',
              '/*/*/FunctionalDatabaseColdReopenTests/OtherMethod',
              '/*/*/(FunctionalDatabaseColdReopenTests|FunctionalDatabaseGateTests)/*',
              '/*/*/CrossTenantClusterWholeFlowTests/*',
              '/*/*/CrossTenantClusterWholeFlowTests*/ForeignWritesScansAndIndexesAreDeniedAndAuthorizedReceiptRemainsStable',
              currentModelFilters[6] + '*',
              currentModelFilters[6] + ' ',
              '/*/*/(CrossTenantClusterWholeFlowTests|PartitionQueryPublicValidationTests)/*',
              '/*/*/PartitionQueryPublicValidationTests*/*',
              currentModelFilters[5] + ' ',
              '/*/*/PartitionQueryPublicValidationTests/AcPquery001InvalidVersionAndLeafOverflowAreValidationWithHealthyFollowup',
              '/*/*/PartitionQueryPublicValidationTests/AcPquery005OversizedRequestAndCancelledSdkAndMcpCallsHaveHealthyFollowups',
              '/*/*/(McpPersistedAuthorizationTests|PartitionQueryPublicValidationTests)/*',
              '/*/*/AspireContainerFaultCommandTests/*',
              currentModelFilters[3] + ' ',
              currentModelFilters[3].replace('AspireContainerFaultCommandTests/', 'AspireContainerFaultCommandTests*/'),
              currentModelFilters[3] + '*',
              '/*/*/AggregateReplayClusterLeaderLossTests/*',
              currentModelFilters[4] + ' ',
              currentModelFilters[4].replace('AggregateReplayClusterLeaderLossTests/', 'AggregateReplayClusterLeaderLossTests*/'),
              currentModelFilters[4] + '*',
              '/*/*/AggregateReplayClusterLeaderLossTests/AcEvent008SnapshotTailCommandRecoversAcrossLeaderLossAndContainerRestart',
              '/*/*/(AspireContainerFaultCommandTests|AggregateReplayClusterLeaderLossTests)/*',
              '/*/*/GraphSearchPathPolicyClusterTests*/*',
              '/*/*/(GraphSearchPathPolicyClusterTests|SqlParserAdmissionClusterWholeFlowTests)/*',
              currentModelFilters[1] + ' ',
              currentModelFilters[1].replace(')/*', '|OtherCase)/*'),
              currentModelFilters[2] + ' ',
              currentModelFilters[2].replace(')/*', '|OtherCase)/*'),
              currentModelFilters[2].replace('SqlParserAdmissionClusterWholeFlowTests|GraphSearchPathPolicyClusterTests',
                'GraphSearchPathPolicyClusterTests|SqlParserAdmissionClusterWholeFlowTests'),
              currentModelFilters[2].replace('SqlParserAdmissionClusterWholeFlowTests|', ''),
              currentModelFilters[2].replace('StrongReadSessionAbsenceClusterTests', 'StrongReadSessionAbsenceClusterTests*')]) {
              let rejected = false;
              try { nativeSelection([localArgs[0], '--KeyLoadTests:Filter=' + badFilter, localArgs[2]], {}); }
              catch (failure) {
                if (failure.message !== 'Invalid native local CLUSTER image selection.') throw failure;
                rejected = true;
              }
              if (!rejected) throw new Error('Unsupported current model cohort selector was accepted.');
            }
            for (const addedIndex of [9]) {
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
