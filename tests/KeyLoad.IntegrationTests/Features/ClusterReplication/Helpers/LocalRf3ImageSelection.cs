using System.Text.Json;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Admits the explicit local image selector without treating a shared image reference as local proof.</summary>
internal static class LocalRf3ImageSelection
{
    private const string ProvenanceEnvironment = "KEYLOAD_IMAGE_PROVENANCE";
    private const string ReceiptEnvironment = "KEYLOAD_LOCAL_IMAGE_RECEIPT";
    private const string ReferenceEnvironment = "KeyLoad__ContainerImages__Server";
    private const string GitHubReceiptEnvironment = "KEYLOAD_IMAGE_RECEIPT";
    private const string GitHubRevisionEnvironment = "GITHUB_SHA";
    private const string GitHubActionsEnvironment = "GITHUB_ACTIONS";
    private const string ChildEnvironment = "KEYLOAD_LOCAL_RF3_IMAGE_CHILD";
    private const string NativeCoverageArgumentsEnvironment = "KEYLOAD_TUNIT_NATIVE_COVERAGE_ARGUMENTS";
    private const string NativeArgumentsEnvironment = "KEYLOAD_TUNIT_LOCAL_RF3_IMAGE_ARGUMENTS";
    private const string Rf3SuiteArgument = "--KeyLoadTests:Suite=rf3";
    private const string RejectionFilterArgument = "--KeyLoadTests:Filter=/*/*/RelationalSqlRf3JoinRejectionTests/*";
    private const string PublicModelFlowFilterArgument = "--KeyLoadTests:Filter=/*/*/(TypedEndpointJoinRf3Tests|DeclarativeCompositionReverseQuotaRf3Tests|SubscriptionPhasePublicColdTests|BlobSupersessionPublicTests|BlobPartialReadBoundaryPublicTests|NativeTextProtectedPublicColdTests|CurrentOwnershipTokenAfterMovementRf3Tests)/*";
    private const string ExpandedPublicModelFlowFilterArgument = "--KeyLoadTests:Filter=/*/*/(TypedEndpointJoinRf3Tests|DeclarativeCompositionReverseQuotaRf3Tests|SubscriptionPhasePublicColdTests|BlobSupersessionPublicTests|BlobPartialReadBoundaryPublicTests|NativeTextProtectedPublicColdTests|CurrentOwnershipTokenAfterMovementRf3Tests|QueueLifecyclePublicRf3Tests|PhysicalShardPartitionMovementRf3Tests|TenantPackingActivationRf3Tests|BlobBoundedRetirementPublicTests|BlobPublicCancellationTests|BlobReadPolicyPublicTests|BlobReservationQuotaPublicTests|NativeTextNestedWildcardPublicTests|NativeTextRawReadPolicyPublicTests|NativeTextRequestRefusalPublicTests|NativeTextRowOwnershipPublicTests)/*";
    private const string SuccessorPublicModelFlowFilterArgument = "--KeyLoadTests:Filter=/*/*/(TypedEndpointJoinRf3Tests|DeclarativeCompositionReverseQuotaRf3Tests|SubscriptionPhasePublicColdTests|BlobSupersessionPublicTests|BlobPartialReadBoundaryPublicTests|NativeTextProtectedPublicColdTests|CurrentOwnershipTokenAfterMovementRf3Tests|QueueLifecyclePublicRf3Tests|PhysicalShardPartitionMovementRf3Tests|TenantPackingActivationRf3Tests|BlobBoundedRetirementPublicTests|BlobPublicCancellationTests|BlobReadPolicyPublicTests|BlobReservationQuotaPublicTests|NativeTextNestedWildcardPublicTests|NativeTextRawReadPolicyPublicTests|NativeTextRequestRefusalPublicTests|NativeTextRowOwnershipPublicTests|GraphVertexPublicWholeFlowTests|QueueLeaseFencingRf3Tests|PartitionMovementFencedAbortRf3Tests|TenantDedicatedOwnerRf3Tests|PostgresNativeSessionRf3Tests|BlobRowCreatorPublicTests|BlobRejectedContentPublicTests|BlobListRowPolicyPublicTests|NativeTextNormalizationPublicTests|NativeTextDistinctTermsPublicTests)/*";
    private const string FinalPublicCohortFilterArgument = "--KeyLoadTests:Filter=/*/*/(BlobActiveUploadSlotPublicTests|BlobArchiveActivePublicTests|BlobBoundedRetirementPublicTests|BlobCompleteCasPublicTests|BlobEmptySlotPublicTests|BlobListRowPolicyPublicTests|BlobObjectKeyLifetimePublicTests|BlobPartialReadBoundaryPublicTests|BlobPublicCancellationTests|BlobReadPolicyPublicTests|BlobRejectedContentPublicTests|BlobReservationQuotaPublicTests|BlobRestoredReclaimPublicTests|BlobRowCreatorPublicTests|BlobScopedUploadIdentityPublicTests|BlobSupersessionPublicTests|CurrentOwnershipTokenAfterMovementRf3Tests|DeclarativeCompositionReverseQuotaRf3Tests|GraphVertexPublicWholeFlowTests|NativeTextDistinctTermsPublicTests|NativeTextEmptyMaintainedPublicTests|NativeTextFreshSchemaGenerationPublicTests|NativeTextNestedWildcardPublicTests|NativeTextNonStringTransitionsPublicTests|NativeTextNormalizationPublicTests|NativeTextPartitionGenerationPublicTests|NativeTextProtectedPublicColdTests|NativeTextRawReadPolicyPublicTests|NativeTextRequestRefusalPublicTests|NativeTextRowOwnershipPublicTests|NativeTextSelectedSchemaPublicTests|PartitionMovementFencedAbortRf3Tests|PhysicalShardPartitionMovementRf3Tests|PostgresDecimalRf3WholeFlowTests|PostgresErrorReplyReceiverRf3Tests|PostgresInt16Rf3WholeFlowTests|PostgresNativeSessionRf3Tests|PostgresPortalOwnershipRf3Tests|PostgresPositiveExecuteRf3Tests|ProjectionPublicColdTests|QueueLeaseFencingRf3Tests|QueueLifecyclePublicRf3Tests|SqlFetchCountRf3WholeFlowTests|SqlNullDistinctRf3WholeFlowTests|SqlNullOrderingRf3WholeFlowTests|SqlOffsetRf3WholeFlowTests|SubscriptionPhasePublicColdTests|TenantCapacityPlacementRf3Tests|TenantDedicatedOwnerRf3Tests|TenantPackingActivationRf3Tests|TypedEndpointJoinRf3Tests)/*";
    private const string SuccessorPublicCohortFilterArgument = "--KeyLoadTests:Filter=/*/*/(BlobActiveUploadSlotPublicTests|BlobArchiveActivePublicTests|BlobBoundedRetirementPublicTests|BlobCompleteCasPublicTests|BlobEmptySlotPublicTests|BlobListRowPolicyPublicTests|BlobObjectKeyLifetimePublicTests|BlobPartialReadBoundaryPublicTests|BlobPublicCancellationTests|BlobReadPolicyPublicTests|BlobRejectedContentPublicTests|BlobReservationQuotaPublicTests|BlobRestoredReclaimPublicTests|BlobRowCreatorPublicTests|BlobScopedUploadIdentityPublicTests|BlobSupersessionPublicTests|CurrentOwnershipTokenAfterMovementRf3Tests|DeclarativeCompositionReverseQuotaRf3Tests|GraphVertexPublicWholeFlowTests|NativeTextDistinctTermsPublicTests|NativeTextEmptyMaintainedPublicTests|NativeTextFreshSchemaGenerationPublicTests|NativeTextNestedWildcardPublicTests|NativeTextNonStringTransitionsPublicTests|NativeTextNormalizationPublicTests|NativeTextPartitionGenerationPublicTests|NativeTextProtectedPublicColdTests|NativeTextRawReadPolicyPublicTests|NativeTextRequestRefusalPublicTests|NativeTextRowOwnershipPublicTests|NativeTextSelectedSchemaPublicTests|PartitionMovementFencedAbortRf3Tests|PhysicalShardPartitionMovementRf3Tests|PostgresDecimalRf3WholeFlowTests|PostgresErrorReplyReceiverRf3Tests|PostgresNativeSessionRf3Tests|PostgresPortalOwnershipRf3Tests|PostgresPositiveExecuteRf3Tests|ProjectionPublicColdTests|QueueLeaseFencingRf3Tests|QueueLifecyclePublicRf3Tests|SqlFetchCountRf3WholeFlowTests|SqlNullDistinctRf3WholeFlowTests|SqlNullOrderingRf3WholeFlowTests|SqlOffsetRf3WholeFlowTests|SubscriptionPhasePublicColdTests|TenantCapacityPlacementRf3Tests|TenantDedicatedOwnerRf3Tests|TenantPackingActivationRf3Tests|TypedEndpointJoinRf3Tests)/*";
    private const string PublicCohortFilterArgument = "--KeyLoadTests:Filter=/*/*/(BlobArchiveActivePublicTests|BlobBoundedRetirementPublicTests|BlobListRowPolicyPublicTests|BlobPartialReadBoundaryPublicTests|BlobPublicCancellationTests|BlobReadPolicyPublicTests|BlobRejectedContentPublicTests|BlobReservationQuotaPublicTests|BlobRestoredReclaimPublicTests|BlobRowCreatorPublicTests|BlobSupersessionPublicTests|CurrentOwnershipTokenAfterMovementRf3Tests|DeclarativeCompositionReverseQuotaRf3Tests|GraphVertexPublicWholeFlowTests|NativeTextDistinctTermsPublicTests|NativeTextNestedWildcardPublicTests|NativeTextNormalizationPublicTests|NativeTextProtectedPublicColdTests|NativeTextRawReadPolicyPublicTests|NativeTextRequestRefusalPublicTests|NativeTextRowOwnershipPublicTests|NativeTextSelectedSchemaPublicTests|PartitionMovementFencedAbortRf3Tests|PhysicalShardPartitionMovementRf3Tests|PostgresNativeSessionRf3Tests|PostgresPortalOwnershipRf3Tests|PostgresPositiveExecuteRf3Tests|ProjectionPublicColdTests|QueueLeaseFencingRf3Tests|QueueLifecyclePublicRf3Tests|SqlFetchCountRf3WholeFlowTests|SqlNullOrderingRf3WholeFlowTests|SqlOffsetRf3WholeFlowTests|SubscriptionPhasePublicColdTests|TenantCapacityPlacementRf3Tests|TenantDedicatedOwnerRf3Tests|TenantPackingActivationRf3Tests|TypedEndpointJoinRf3Tests)/*";
    private const string TenantCapacityPlacementFilterArgument = "--KeyLoadTests:Filter=/*/*/TenantCapacityPlacementRf3Tests/*";
    private const string MembershipFilterArgument = "--KeyLoadTests:Filter=/*/*/TwoRf3MembershipProfileTests/*";
    private const string ConnectionFilterArgument = "--KeyLoadTests:Filter=/*/*/(ConnectionRf3SequentialTests|ConnectionRf3OverlapTests|ConnectionRf3AuthorizationTests)/*";
    private const string StandardFilterArgument = "--KeyLoadTests:Filter=/*/*/(PartitionQueryMcpSchemaTests|RelationalSqlRf3JoinTests|RelationalSqlRf3JoinAuthorizationTests|RelationalSqlRf3JoinBudgetTests|RelationalSqlRf3JoinCancellationTests|RelationalSqlRf3JoinReadCutTests)/*";
    private const string EmptySnapshotFilterArgument = "--KeyLoadTests:Filter=/*/*/EmptyReplicaSnapshotRf3Tests/*";
    private const string RetainedSnapshotFilterArgument = "--KeyLoadTests:Filter=/*/*/ClusterTests/RetainedReplicaCatchesUpThroughNativeSnapshotAndKeepsCommandOutcomes";
    private const string NativeTextFilterArgument = "--KeyLoadTests:Filter=/*/*/(NativeTextAsyncRf3Tests)|(NativeTextMaintenanceRf3Tests)|(NativeTextRf3LeaderLossTests)|(NativeTextRf3Tests)|(NativeTextWaitRf3Tests)/*";
    private const string RemoteTransferFilterArgument = "--KeyLoadTests:Filter=/*/*/RemoteTransferDistinctOwnerTests/ActualDistinctOwnersRetainAcceptReceiptAcrossTwoColdRestartsAndBDoesNotResurrectAcknowledgedMessage";
    private const string CapabilityOmissionFilterArgument = "--KeyLoadTests:Filter=/*/*/NativeCapabilityOmissionRf3Tests/ForegroundSdkOrOfficialMcpOmittedCapabilityRefusesThenSameOwnerRepairAndColdReplay";
    private const string GraphIncomingFilterArgument = "--KeyLoadTests:Filter=/*/*/GraphIncomingRf3Tests/PersistedSourceGrantHidesCrossRowsAndTargetGraphDenialIsExplicit";
    private const string SignedPurposeFilterArgument = "--KeyLoadTests:Filter=/*/*/NativeSignedPurposeRf3Tests/ForegroundSdkOrOfficialMcpSignedPurposeRefusesThenExactRepairReplayAndTwoColdCuts";
    private const string NullQueueDeadlineFilterArgument = "--KeyLoadTests:Filter=/*/*/QueueDeadlineAutonomousRf3Tests/NullSelectedServicePreservesScheduledStateReceiptsAcrossColdThenPublicClaimAndHealthyAck";
    private const string EventFeedOpenFilterArgument = "--KeyLoadTests:Filter=/*/*/EventFeedFirstOpenRf3Tests/ActualSixOwnerFirstOpenPinsReplaySourceRevocationRepairAndSameRootCold";
    private const string ResourcePolicyFilterArgument = "--KeyLoadTests:Filter=/*/*/ResourcePolicyUpdateRf3Tests/AcRpol004PolicyAckFencesReadersCursorsAndStaleRetriedUpdatesAcrossRf3";
    private const string McpCurrentAuthorityFilterArgument = "--KeyLoadTests:Filter=/*/*/McpPersistedAuthorizationTests/AcMcp002CurrentPersistedPrincipalControlsEachCallAfterDiscovery";
    private const string McpCredentialAuthorityFilterArgument = "--KeyLoadTests:Filter=/*/*/McpCredentialLifecycleTests/AcMcp002PersistedCredentialChangesRejectTheClientAfterDiscovery";
    private const string McpInitialCredentialFilterArgument = "--KeyLoadTests:Filter=/*/*/McpCredentialLifecycleTests/AcMcp002NativeDiscoveryRejectsMissingAndUnconfiguredCredentials";
    private const string ProjectionReplayFilterArgument = "--KeyLoadTests:Filter=/*/*/EventProjectionRf3Tests/AcLineage001ExactReplayConflictAndStaleInputPreserveOnlyTheCommittedProjection";
    private const string ProjectionPolicyFilterArgument = "--KeyLoadTests:Filter=/*/*/EventProjectionRf3Tests/AcLineage002SourceHiddenFromPersistedReaderCannotProduceVisibleDerivedVector";
    private const string ProjectionColdFilterArgument = "--KeyLoadTests:Filter=/*/*/EventProjectionRf3Tests/AcLineage003CommittedProjectionReceiptAndLiveVectorSurviveLeaderLossAndRestart";
    private const string RemoteAnnPublicationFilterArgument = "--KeyLoadTests:Filter=/*/*/RemoteAnnPublicationRf3Tests/DifferentVoterSdkAndOfficialMcpObserveActualPublicationAcrossMovePolicyRepairAndTwoColdRoots";
    private const string GraphEventLineageAuthenticationFilterArgument = "--KeyLoadTests:Filter=/*/*/(AuthenticationExecutionTests*)|(EventReferenceSelectRf3Tests*)|(NativeRemoteGraphRf3Tests*)|(ComposedProjectionLineageRf3Tests*)/(RealHttpSdkAndOfficialMcpAuthenticateOncePerRequestThenFreshPolicyRefusalRepairReplayAndTwoColdCuts)|(AcKl095EventReferenceSelectRepairsPersistedUseAndMissingTargetThenReplaysLiteralSdkMcpRowsAcrossTwoColdCuts)|(GenuineSixOwnerCurrentSourceRightsRefusalRestoresOriginalGraphRequestThenFullReceiptsSurviveTwoColdReopens)|(AcKl048ComposedSourceProjectionAndPredicateUseRevokeRepairPreserveSdkMcpRowsAndReceiptsAcrossTwoColdCuts)";
    private const string OriginalRefusalCompositionFilterArgument = "--KeyLoadTests:Filter=/*/*/(AuthenticationExecutionTests*)|(EventReferenceSelectRf3Tests*)|(NativeRemoteGraphRf3Tests*)|(ComposedProjectionLineageRf3Tests*)|(CrossTenantRf3WholeFlowTests)|(RequestCqrsAuthorityFaultTests)/(RealHttpSdkAndOfficialMcpAuthenticateOncePerRequestThenFreshPolicyRefusalRepairReplayAndTwoColdCuts)|(AcKl095EventReferenceSelectRepairsPersistedUseAndMissingTargetThenReplaysLiteralSdkMcpRowsAcrossTwoColdCuts)|(GenuineSixOwnerCurrentSourceRightsRefusalRestoresOriginalGraphRequestThenFullReceiptsSurviveTwoColdReopens)|(AcKl048ComposedSourceProjectionAndPredicateUseRevokeRepairPreserveSdkMcpRowsAndReceiptsAcrossTwoColdCuts)|(Kl015ForeignWritesScansAndIndexesAreDeniedAndAuthorizedReceiptRemainsStable)|(SdkWriteHeldAcrossPersistedRevocationIsUnauthorizedWithoutEffects)|(OfficialMcpWriteHeldAcrossPersistedRevocationIsUnauthorizedWithoutEffects)";
    private const string ReleaseDistributedQueryAuthorityRf3FilterArgument = "--KeyLoadTests:Filter=/*/*/DistributedQueryAuthorityRf3Tests/*";
    private const string ReleaseEarlyAdmissionQualificationFilterArgument = "--KeyLoadTests:Filter=/*/*/EarlyAdmissionQualificationTests/*";
    private const string ReleaseMetadataBootstrapRf3FilterArgument = "--KeyLoadTests:Filter=/*/*/MetadataBootstrapRf3Tests/*";
    private const string ReleaseQueryRuleOptimizationRf3FilterArgument = "--KeyLoadTests:Filter=/*/*/QueryRuleOptimizationRf3Tests/*";
    private const string ReleaseStrongReadSessionAbsenceRf3FilterArgument = "--KeyLoadTests:Filter=/*/*/StrongReadSessionAbsenceRf3Tests/*";
    private const string ReleaseTimeSeriesChunkQualificationRf3FilterArgument = "--KeyLoadTests:Filter=/*/*/TimeSeriesChunkQualificationRf3Tests/*";
    private const string ReleaseCombinedFilterArgument = "--KeyLoadTests:Filter=/*/*/(DistributedQueryAuthorityRf3Tests|EarlyAdmissionQualificationTests|MetadataBootstrapRf3Tests|QueryRuleOptimizationRf3Tests|StrongReadSessionAbsenceRf3Tests|TimeSeriesChunkQualificationRf3Tests)/*";
    private const string CurrentSafeProjectorFilterArgument = "--KeyLoadTests:Filter=/*/*/SafeProjectorOperationRf3Tests/*";
    private const string CurrentSqlParserAdmissionFilterArgument = "--KeyLoadTests:Filter=/*/*/SqlParserAdmissionRf3WholeFlowTests/*";
    private const string CurrentGraphSearchPathPolicyFilterArgument = "--KeyLoadTests:Filter=/*/*/GraphSearchPathPolicyRf3Tests/*";
    private const string CurrentSampleChunkCorrectionFilterArgument = "--KeyLoadTests:Filter=/*/*/SampleChunkCorrectionPredicateRf3Tests/*";
    private const string CurrentSqlGraphPublicFilterArgument = "--KeyLoadTests:Filter=/*/*/(SqlParserAdmissionRf3WholeFlowTests|GraphSearchPathPolicyRf3Tests)/*";
    private const string CurrentSqlGraphSamplePublicFilterArgument = "--KeyLoadTests:Filter=/*/*/(SqlParserAdmissionRf3WholeFlowTests|GraphSearchPathPolicyRf3Tests|SampleChunkCorrectionPredicateRf3Tests)/*";
    private const string CurrentGraphSamplePublicFilterArgument = "--KeyLoadTests:Filter=/*/*/(GraphSearchPathPolicyRf3Tests|SampleChunkCorrectionPredicateRf3Tests)/*";
    private const string CurrentFoundationRf3FilterArgument = "--KeyLoadTests:Filter=/*/*/(NativeDatabaseMigrationRf3Tests|SqlParserAdmissionRf3WholeFlowTests|GraphSearchPathPolicyRf3Tests|RequestCqrsPhaseFaultTests|RequestCqrsAuthorityFaultTests|RequestCqrsRf3FollowerRestartTests|SagaDueRf3Tests|SagaTimeoutRf3Tests|SampleChunkCorrectionPredicateRf3Tests|ConnectionRf3AuthorizationTests|DocumentSessionReadRf3Tests|MetadataBootstrapRf3Tests|PartitionMovementPublicParentFlowTests|PhysicalOwnerRegistrationRf3Tests|ProtectedDocumentRf3Tests|QueryContextSessionRf3Tests|StrongReadSessionAbsenceRf3Tests)/*";
    private const string CurrentGraphSearchLineageFilterArgument = "--KeyLoadTests:Filter=/*/*/GraphSearchLineageRf3Tests/*";
    private const string CurrentAtomicTriadFilterArgument = "--KeyLoadTests:Filter=/*/*/AtomicTriadStagedRf3Tests/*";
    private const string CurrentCompetingInboxFilterArgument = "--KeyLoadTests:Filter=/*/*/CompetingInboxPublicTests/*";
    private const string CurrentQueueViewLeaseFilterArgument = "--KeyLoadTests:Filter=/*/*/QueueViewLeaseIsolationTests/*";
    private const string CurrentCombinedFilterArgument = "--KeyLoadTests:Filter=/*/*/(SafeProjectorOperationRf3Tests|SqlParserAdmissionRf3WholeFlowTests|GraphSearchLineageRf3Tests|AtomicTriadStagedRf3Tests|CompetingInboxPublicTests|QueueViewLeaseIsolationTests)/*";
    private const string NativeMigrationFilterArgument = "--KeyLoadTests:Filter=/*/*/NativeDatabaseMigrationRf3Tests/*";
    private const string FoundationCombinedFilterArgument = "--KeyLoadTests:Filter=/*/*/(SafeProjectorOperationRf3Tests|SqlParserAdmissionRf3WholeFlowTests|GraphSearchLineageRf3Tests|AtomicTriadStagedRf3Tests|CompetingInboxPublicTests|QueueViewLeaseIsolationTests|NativeDatabaseMigrationRf3Tests)/*";
    private const string SlowBodyReturnFilterArgument = "--KeyLoadTests:Filter=/*/*/SlowBodyAdmissionReturnTests/*";
    private const string RowRevisionAdmissionFilterArgument = "--KeyLoadTests:Filter=/*/*/RowRevisionInferenceRf3Tests/*";
    private const string FoundationAdmissionRepairFilterArgument = "--KeyLoadTests:Filter=/*/*/(NativeDatabaseMigrationRf3Tests|SlowBodyAdmissionReturnTests|RowRevisionInferenceRf3Tests)/*";
    private const string CatalogBootstrapAuthorityFilterArgument = "--KeyLoadTests:Filter=/*/*/CatalogBootstrapAuthorityRf3Tests/*";
    private const string FoundationAdmissionAuthorityFilterArgument = "--KeyLoadTests:Filter=/*/*/(SlowBodyAdmissionReturnTests|RowRevisionInferenceRf3Tests|CatalogBootstrapAuthorityRf3Tests)/*";
    private const string FoundationTelemetryFilterArgument = "--KeyLoadTests:Filter=/*/*/FoundationOtlpRf3Tests/NativeDashboardReceivesActualRf3SdkMcpPolicyRepairTracesAndPrivateFreeRejectionLogs";
    private const string AspireFaultFilterArgument = "--KeyLoadTests:Filter=/*/*/AspireContainerFaultCommandTests/WrongStaleAndForeignIdsRefuseWithoutEffectsBeforeHealthySdkMcpRestart";
    private const string AcceptedRestartCancellationFilterArgument = "--KeyLoadTests:Filter=/*/*/AggregateReplayRf3LeaderLossTests/AcEvent008AcceptedRestartCancellationResumesSameReplacementAndPreservesReplay";
    private const string OriginalRetireAspireFilterArgument = "--KeyLoadTests:Filter=/*/*/ControlledPartitionMovementNativeRetireTests/OriginalNativeRetireCallerCancellationAndHostStopRetainAuthorityThenResumeReplayMixedModelsAndTwoColdOwners";
    private const string FunctionalSingleFilterArgument = "--KeyLoadTests:Filter=/*/*/FunctionalDatabaseGateTests/SingleInstanceRunsEveryModelThroughSdkMcpAndSqlWithPersistedUsers";
    private const string FunctionalClusterFilterArgument = "--KeyLoadTests:Filter=/*/*/FunctionalDatabaseGateTests/ThreeNodeClusterRunsSameEveryModelScenarioThroughSdkMcpAndSql";
    private const string PartitionQueryValidationFilterArgument = "--KeyLoadTests:Filter=/*/*/PartitionQueryPublicValidationTests/*";
    private const string CrossTenantWholeFlowFilterArgument = "--KeyLoadTests:Filter=/*/*/CrossTenantRf3WholeFlowTests/Kl015ForeignWritesScansAndIndexesAreDeniedAndAuthorizedReceiptRemainsStable";
    private const string RemotePartitionQueryFilterArgument = "--KeyLoadTests:Filter=/*/*/RemotePartitionQueryRf3Tests/AcPqueryRemote001To005DestinationDenialLiteralMergeReceiptReplayAndOwnedRestart";
    private const string FunctionalSingleColdFilterArgument = "--KeyLoadTests:Filter=/*/*/FunctionalDatabaseColdReopenTests/SingleInstanceColdReopensAllModelsAndReplaysOriginalPublicCommands";
    private const string FunctionalClusterColdFilterArgument = "--KeyLoadTests:Filter=/*/*/FunctionalDatabaseColdReopenTests/ThreeNodeColdReopensAllModelsAndReplaysOriginalPublicCommands";
    private const string NativeImageEnrollmentFilterArgument = "--KeyLoadTests:Filter=/*/*/(PhysicalOwnerRegistrationRf3Tests|RemotePartitionQueryRf3Tests|RequestCqrsPhaseFaultTests|RequestCqrsRf3FollowerRestartTests)/*";
    private const string ControlledBlobLifetimeReadFilterArgument = "--KeyLoadTests:Filter=/*/*/ControlledBlobLifetimeReadRf3Tests/HeldNativeRepliesRequireFreshSourceAuthorityAndSettleBeforeTwoColdBlobReadWaves";
    private const string NativeBlobWholeParentFilterArgument = "--KeyLoadTests:Filter=/*/*/(PartitionMovementBlobWireNativeTests|NativeBlobWholeParentQualificationCases)/*";
    private const string DropTableLifecycleFilterArgument = "--KeyLoadTests:Filter=/*/*/DropTableLifecycleRf3Tests/PublicDropRecreateRetainsAuthorityAndReplayAcrossTwoColdRf3Waves";
    private const string ResourcePartitionInventoryFilterArgument = "--KeyLoadTests:Filter=/*/*/ResourcePartitionInventoryRf3Tests/AuthenticatedConservativeInventoryRetainsReplayRollbackAndTwoColdOwners";
    private const string PublicPartitionMovementFilterArgument = "--KeyLoadTests:Filter=/*/*/PartitionMovementPublicParentFlowTests/ActualPublicParentMovesAllLinkedModelsAndOriginalReceiptsAcrossTwoRf3OwnersAndBackAfterColdReplay";
    private const string ErasedFollowerSnapshotFilterArgument = "--KeyLoadTests:Filter=/*/*/EmptyReplicaSnapshotRf3Tests/ErasedFollowerInstallsNativeSnapshotAndOrderedTailBeforeCompletePublicReplayAndHealthyContinuation";
    private const string SdkRevocationWriteFilterArgument = "--KeyLoadTests:Filter=/*/*/RequestCqrsAuthorityFaultTests/SdkWriteHeldAcrossPersistedRevocationIsUnauthorizedWithoutEffects";
    private const string EnabledArgument = "--KeyLoadTests:LocalRf3Image:Enabled=true";
    private const string EnabledValue = "true";
    private const int MaximumNativeArgumentCharacters = 4096;
    internal const string Provenance = "local-development";
    internal const string Repository = "keyload/local-server";
    private const int MaximumReceiptPathCharacters = 256;

    internal sealed record Selection(string Reference, string Tag, string Receipt, string ChildMarker)
    {
        internal string[] CreateWaveArguments() =>
        [
            "--KEYLOAD_IMAGE_PROVENANCE=" + Provenance,
            "--KeyLoad:ContainerImages:Server=" + Reference,
            "--KEYLOAD_LOCAL_IMAGE_RECEIPT=" + Receipt,
            "--KEYLOAD_LOCAL_RF3_IMAGE_CHILD=" + ChildMarker
        ];
    }

    internal static Selection? Read()
    {
        var provenance = Environment.GetEnvironmentVariable(ProvenanceEnvironment);
        var reference = Environment.GetEnvironmentVariable(ReferenceEnvironment);
        var receipt = Environment.GetEnvironmentVariable(ReceiptEnvironment);
        if (provenance is null && receipt is null)
        {
            return null;
        }
        return CreateSelection(provenance, reference, receipt, Environment.GetEnvironmentVariable(ChildEnvironment),
            requireChild: false, rejectAmbientGithub: true);
    }

    internal static string[]? ReadNativeArgumentsIfSelected()
    {
        var value = Environment.GetEnvironmentVariable(NativeArgumentsEnvironment);
        if (value is null)
        {
            return null;
        }
        if (value.Length is 0 or > MaximumNativeArgumentCharacters)
        {
            throw new InvalidOperationException("The local RF3 image selection is invalid.");
        }
        var arguments = JsonSerializer.Deserialize<string[]>(value);
        if (arguments is not [Rf3SuiteArgument, MembershipFilterArgument or StandardFilterArgument or RejectionFilterArgument or ConnectionFilterArgument or NativeTextFilterArgument or RemoteTransferFilterArgument or EmptySnapshotFilterArgument or CapabilityOmissionFilterArgument
                or RetainedSnapshotFilterArgument or NullQueueDeadlineFilterArgument or GraphIncomingFilterArgument or SignedPurposeFilterArgument or EventFeedOpenFilterArgument or ResourcePolicyFilterArgument or McpCurrentAuthorityFilterArgument or McpCredentialAuthorityFilterArgument or McpInitialCredentialFilterArgument
                or ProjectionReplayFilterArgument or ProjectionPolicyFilterArgument or ProjectionColdFilterArgument or RemoteAnnPublicationFilterArgument or GraphEventLineageAuthenticationFilterArgument or OriginalRefusalCompositionFilterArgument or PublicModelFlowFilterArgument or ExpandedPublicModelFlowFilterArgument or SuccessorPublicModelFlowFilterArgument or TenantCapacityPlacementFilterArgument or PublicCohortFilterArgument or SuccessorPublicCohortFilterArgument or FinalPublicCohortFilterArgument
                or ReleaseDistributedQueryAuthorityRf3FilterArgument or ReleaseEarlyAdmissionQualificationFilterArgument or ReleaseMetadataBootstrapRf3FilterArgument or ReleaseQueryRuleOptimizationRf3FilterArgument or ReleaseStrongReadSessionAbsenceRf3FilterArgument or ReleaseTimeSeriesChunkQualificationRf3FilterArgument or ReleaseCombinedFilterArgument
                or CurrentSafeProjectorFilterArgument or CurrentSqlParserAdmissionFilterArgument or CurrentGraphSearchLineageFilterArgument or CurrentAtomicTriadFilterArgument or CurrentCompetingInboxFilterArgument or CurrentQueueViewLeaseFilterArgument or CurrentCombinedFilterArgument
                or NativeMigrationFilterArgument or FoundationCombinedFilterArgument
                or SlowBodyReturnFilterArgument or RowRevisionAdmissionFilterArgument
                or FoundationAdmissionRepairFilterArgument or CatalogBootstrapAuthorityFilterArgument
                or FoundationAdmissionAuthorityFilterArgument or FoundationTelemetryFilterArgument
                or AspireFaultFilterArgument or AcceptedRestartCancellationFilterArgument or OriginalRetireAspireFilterArgument
                or FunctionalSingleFilterArgument or FunctionalClusterFilterArgument or PartitionQueryValidationFilterArgument
                or CrossTenantWholeFlowFilterArgument
                or RemotePartitionQueryFilterArgument or FunctionalSingleColdFilterArgument or FunctionalClusterColdFilterArgument or NativeImageEnrollmentFilterArgument
                or ControlledBlobLifetimeReadFilterArgument
                or NativeBlobWholeParentFilterArgument
                or ResourcePartitionInventoryFilterArgument
                or DropTableLifecycleFilterArgument
                or PublicPartitionMovementFilterArgument
                or ErasedFollowerSnapshotFilterArgument
                or SdkRevocationWriteFilterArgument
                or CurrentGraphSearchPathPolicyFilterArgument or CurrentSampleChunkCorrectionFilterArgument or CurrentSqlGraphPublicFilterArgument or CurrentSqlGraphSamplePublicFilterArgument or CurrentGraphSamplePublicFilterArgument or CurrentFoundationRf3FilterArgument, EnabledArgument]
            || Environment.GetEnvironmentVariable(NativeCoverageArgumentsEnvironment) is not null
            || HasAmbientGithubIdentity()
            || Environment.GetEnvironmentVariable(ProvenanceEnvironment) is not null
            || Environment.GetEnvironmentVariable(ReceiptEnvironment) is not null
            || Environment.GetEnvironmentVariable(ReferenceEnvironment) is not null
            || Environment.GetEnvironmentVariable(ChildEnvironment) is not null)
        {
            throw new InvalidOperationException("The local RF3 image selection is invalid.");
        }
        return arguments;
    }

    internal static Selection FromRunnerConfiguration(IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        values.TryGetValue(ProvenanceEnvironment, out var provenance);
        values.TryGetValue(ReferenceEnvironment, out var reference);
        values.TryGetValue(ReceiptEnvironment, out var receipt);
        values.TryGetValue(ChildEnvironment, out var child);
        if (values.TryGetValue(GitHubReceiptEnvironment, out var githubReceipt) && !string.IsNullOrWhiteSpace(githubReceipt)
            || values.TryGetValue(GitHubRevisionEnvironment, out var githubRevision) && !string.IsNullOrWhiteSpace(githubRevision)
            || values.TryGetValue(GitHubActionsEnvironment, out var githubActions) && !string.IsNullOrWhiteSpace(githubActions))
        {
            throw new InvalidOperationException("The local RF3 image identity is invalid.");
        }
        return CreateSelection(provenance, reference, receipt, child, requireChild: true, rejectAmbientGithub: false);
    }

    private static Selection CreateSelection(string? provenance, string? reference, string? receipt, string? child,
        bool requireChild, bool rejectAmbientGithub)
    {
        if (provenance != Provenance || reference is null || receipt is null
            || requireChild && child != EnabledValue
            || rejectAmbientGithub && HasAmbientGithubIdentity()
            || receipt.Length is 0 or > MaximumReceiptPathCharacters || Path.IsPathRooted(receipt)
            || receipt.Contains('\\', StringComparison.Ordinal) || !reference.StartsWith(Repository + ":local-", StringComparison.Ordinal)
            || !LocalRf3ImageReferenceValidation.IsCanonicalReferenceAndReceipt(reference, receipt))
        {
            throw new InvalidOperationException("The local RF3 image identity is invalid.");
        }
        var tag = reference[(reference.IndexOf(':', StringComparison.Ordinal) + 1)..];
        return new(reference, tag, receipt, child ?? EnabledValue);
    }

    private static bool HasAmbientGithubIdentity()
        => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(GitHubReceiptEnvironment))
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(GitHubRevisionEnvironment))
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(GitHubActionsEnvironment));

}
