using System.Text.Json;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Admits the explicit local image selector without treating a shared image reference as local proof.</summary>
internal static class LocalClusterImageSelection
{
    private const string ProvenanceEnvironment = "KEYLOAD_IMAGE_PROVENANCE";
    private const string ReceiptEnvironment = "KEYLOAD_LOCAL_IMAGE_RECEIPT";
    private const string ReferenceEnvironment = "KeyLoad__ContainerImages__Server";
    private const string GitHubReceiptEnvironment = "KEYLOAD_IMAGE_RECEIPT";
    private const string GitHubRevisionEnvironment = "GITHUB_SHA";
    private const string GitHubActionsEnvironment = "GITHUB_ACTIONS";
    private const string ChildEnvironment = "KEYLOAD_LOCAL_CLUSTER_IMAGE_CHILD";
    private const string NativeCoverageArgumentsEnvironment = "KEYLOAD_TUNIT_NATIVE_COVERAGE_ARGUMENTS";
    private const string NativeArgumentsEnvironment = "KEYLOAD_TUNIT_LOCAL_CLUSTER_IMAGE_ARGUMENTS";
    private const string ClusterSuiteArgument = "--KeyLoadTests:Suite=cluster";
    private const string RejectionFilterArgument = "--KeyLoadTests:Filter=/*/*/RelationalSqlClusterJoinRejectionTests/*";
    private const string PublicModelFlowFilterArgument = "--KeyLoadTests:Filter=/*/*/(TypedEndpointJoinClusterTests|DeclarativeCompositionReverseQuotaClusterTests|SubscriptionPhasePublicColdTests|BlobSupersessionPublicTests|BlobPartialReadBoundaryPublicTests|NativeTextProtectedPublicColdTests)/*";
    private const string ExpandedPublicModelFlowFilterArgument = "--KeyLoadTests:Filter=/*/*/(TypedEndpointJoinClusterTests|DeclarativeCompositionReverseQuotaClusterTests|SubscriptionPhasePublicColdTests|BlobSupersessionPublicTests|BlobPartialReadBoundaryPublicTests|NativeTextProtectedPublicColdTests|QueueLifecyclePublicClusterTests|BlobBoundedRetirementPublicTests|BlobReadPolicyPublicTests|BlobReservationQuotaPublicTests|NativeTextNestedWildcardPublicTests|NativeTextRawReadPolicyPublicTests|NativeTextRequestRefusalPublicTests|NativeTextRowOwnershipPublicTests)/*";
    private const string SuccessorPublicModelFlowFilterArgument = "--KeyLoadTests:Filter=/*/*/(TypedEndpointJoinClusterTests|DeclarativeCompositionReverseQuotaClusterTests|SubscriptionPhasePublicColdTests|BlobSupersessionPublicTests|BlobPartialReadBoundaryPublicTests|NativeTextProtectedPublicColdTests|QueueLifecyclePublicClusterTests|BlobBoundedRetirementPublicTests|BlobReadPolicyPublicTests|BlobReservationQuotaPublicTests|NativeTextNestedWildcardPublicTests|NativeTextRawReadPolicyPublicTests|NativeTextRequestRefusalPublicTests|NativeTextRowOwnershipPublicTests|GraphVertexPublicWholeFlowTests|QueueLeaseFencingClusterTests|PostgresNativeSessionClusterTests|BlobRowCreatorPublicTests|BlobRejectedContentPublicTests|BlobListRowPolicyPublicTests|NativeTextNormalizationPublicTests|NativeTextDistinctTermsPublicTests)/*";
    private const string FinalPublicCohortFilterArgument = "--KeyLoadTests:Filter=/*/*/(BlobActiveUploadSlotPublicTests|BlobBoundedRetirementPublicTests|BlobCompleteCasPublicTests|BlobEmptySlotPublicTests|BlobListRowPolicyPublicTests|BlobObjectKeyLifetimePublicTests|BlobPartialReadBoundaryPublicTests|BlobReadPolicyPublicTests|BlobRejectedContentPublicTests|BlobReservationQuotaPublicTests|BlobRowCreatorPublicTests|BlobScopedUploadIdentityPublicTests|BlobSupersessionPublicTests|DeclarativeCompositionReverseQuotaClusterTests|GraphVertexPublicWholeFlowTests|NativeTextDistinctTermsPublicTests|NativeTextNestedWildcardPublicTests|NativeTextNonStringTransitionsPublicTests|NativeTextNormalizationPublicTests|NativeTextProtectedPublicColdTests|NativeTextRawReadPolicyPublicTests|NativeTextRequestRefusalPublicTests|NativeTextRowOwnershipPublicTests|PostgresDecimalClusterWholeFlowTests|PostgresErrorReplyReceiverClusterTests|PostgresInt16ClusterWholeFlowTests|PostgresNativeSessionClusterTests|PostgresPortalOwnershipClusterTests|PostgresPositiveExecuteClusterTests|ProjectionPublicColdTests|QueueLeaseFencingClusterTests|QueueLifecyclePublicClusterTests|SqlFetchCountClusterWholeFlowTests|SqlNullDistinctClusterWholeFlowTests|SqlNullOrderingClusterWholeFlowTests|SqlOffsetClusterWholeFlowTests|SubscriptionPhasePublicColdTests|TypedEndpointJoinClusterTests)/*";
    private const string SuccessorPublicCohortFilterArgument = "--KeyLoadTests:Filter=/*/*/(BlobActiveUploadSlotPublicTests|BlobBoundedRetirementPublicTests|BlobCompleteCasPublicTests|BlobEmptySlotPublicTests|BlobListRowPolicyPublicTests|BlobObjectKeyLifetimePublicTests|BlobPartialReadBoundaryPublicTests|BlobReadPolicyPublicTests|BlobRejectedContentPublicTests|BlobReservationQuotaPublicTests|BlobRowCreatorPublicTests|BlobScopedUploadIdentityPublicTests|BlobSupersessionPublicTests|DeclarativeCompositionReverseQuotaClusterTests|GraphVertexPublicWholeFlowTests|NativeTextDistinctTermsPublicTests|NativeTextNestedWildcardPublicTests|NativeTextNonStringTransitionsPublicTests|NativeTextNormalizationPublicTests|NativeTextProtectedPublicColdTests|NativeTextRawReadPolicyPublicTests|NativeTextRequestRefusalPublicTests|NativeTextRowOwnershipPublicTests|PostgresDecimalClusterWholeFlowTests|PostgresErrorReplyReceiverClusterTests|PostgresNativeSessionClusterTests|PostgresPortalOwnershipClusterTests|PostgresPositiveExecuteClusterTests|ProjectionPublicColdTests|QueueLeaseFencingClusterTests|QueueLifecyclePublicClusterTests|SqlFetchCountClusterWholeFlowTests|SqlNullDistinctClusterWholeFlowTests|SqlNullOrderingClusterWholeFlowTests|SqlOffsetClusterWholeFlowTests|SubscriptionPhasePublicColdTests|TypedEndpointJoinClusterTests)/*";
    private const string PublicCohortFilterArgument = "--KeyLoadTests:Filter=/*/*/(BlobBoundedRetirementPublicTests|BlobListRowPolicyPublicTests|BlobPartialReadBoundaryPublicTests|BlobReadPolicyPublicTests|BlobRejectedContentPublicTests|BlobReservationQuotaPublicTests|BlobRowCreatorPublicTests|BlobSupersessionPublicTests|DeclarativeCompositionReverseQuotaClusterTests|GraphVertexPublicWholeFlowTests|NativeTextDistinctTermsPublicTests|NativeTextNestedWildcardPublicTests|NativeTextNormalizationPublicTests|NativeTextProtectedPublicColdTests|NativeTextRawReadPolicyPublicTests|NativeTextRequestRefusalPublicTests|NativeTextRowOwnershipPublicTests|PostgresNativeSessionClusterTests|PostgresPortalOwnershipClusterTests|PostgresPositiveExecuteClusterTests|ProjectionPublicColdTests|QueueLeaseFencingClusterTests|QueueLifecyclePublicClusterTests|SqlFetchCountClusterWholeFlowTests|SqlNullOrderingClusterWholeFlowTests|SqlOffsetClusterWholeFlowTests|SubscriptionPhasePublicColdTests|TypedEndpointJoinClusterTests)/*";
    private const string StandardFilterArgument = "--KeyLoadTests:Filter=/*/*/(PartitionQueryMcpSchemaTests|RelationalSqlClusterJoinTests|RelationalSqlClusterJoinAuthorizationTests|RelationalSqlClusterJoinBudgetTests|RelationalSqlClusterJoinCancellationTests|RelationalSqlClusterJoinReadCutTests)/*";
    private const string RetainedSnapshotFilterArgument = "--KeyLoadTests:Filter=/*/*/ClusterTests/RetainedReplicaCatchesUpThroughNativeSnapshotAndKeepsCommandOutcomes";
    private const string NativeTextFilterArgument = "--KeyLoadTests:Filter=/*/*/(NativeTextAsyncClusterTests)|(NativeTextClusterSiloRestartTests)|(NativeTextClusterTests)|(NativeTextWaitClusterTests)/*";
    private const string GraphIncomingFilterArgument = "--KeyLoadTests:Filter=/*/*/GraphIncomingClusterTests/PersistedSourceGrantHidesCrossRowsAndTargetGraphDenialIsExplicit";
    private const string NullQueueDeadlineFilterArgument = "--KeyLoadTests:Filter=/*/*/QueueDeadlineAutonomousClusterTests/NullSelectedServicePreservesScheduledStateReceiptsAcrossColdThenPublicClaimAndHealthyAck";
    private const string ResourcePolicyFilterArgument = "--KeyLoadTests:Filter=/*/*/ResourcePolicyUpdateClusterTests/AcRpol004PolicyAckFencesReadersCursorsAndStaleRetriedUpdatesAcrossCluster";
    private const string McpCurrentAuthorityFilterArgument = "--KeyLoadTests:Filter=/*/*/McpPersistedAuthorizationTests/AcMcp002CurrentPersistedPrincipalControlsEachCallAfterDiscovery";
    private const string McpCredentialAuthorityFilterArgument = "--KeyLoadTests:Filter=/*/*/McpCredentialLifecycleTests/AcMcp002PersistedCredentialChangesRejectTheClientAfterDiscovery";
    private const string McpInitialCredentialFilterArgument = "--KeyLoadTests:Filter=/*/*/McpCredentialLifecycleTests/AcMcp002NativeDiscoveryRejectsMissingAndUnconfiguredCredentials";
    private const string ProjectionReplayFilterArgument = "--KeyLoadTests:Filter=/*/*/EventProjectionClusterTests/AcLineage001ExactReplayConflictAndStaleInputPreserveOnlyTheCommittedProjection";
    private const string ProjectionPolicyFilterArgument = "--KeyLoadTests:Filter=/*/*/EventProjectionClusterTests/AcLineage002SourceHiddenFromPersistedReaderCannotProduceVisibleDerivedVector";
    private const string ProjectionColdFilterArgument = "--KeyLoadTests:Filter=/*/*/EventProjectionClusterTests/AcLineage003CommittedProjectionReceiptAndLiveVectorSurviveSiloLossAndRejoin";
    private const string GraphEventLineageAuthenticationFilterArgument = "--KeyLoadTests:Filter=/*/*/(AuthenticationExecutionTests*)|(EventReferenceSelectClusterTests*)|(ComposedProjectionLineageClusterTests*)/(RealHttpSdkAndOfficialMcpAuthenticateOncePerRequestThenFreshPolicyRefusalRepairReplayAndTwoColdCuts)|(EventReferenceSelectRepairsPersistedUseAndMissingTargetThenReplaysLiteralSdkMcpRowsAcrossTwoColdCuts)|(GenuineSixOwnerCurrentSourceRightsRefusalRestoresOriginalGraphRequestThenFullReceiptsSurviveTwoColdReopens)|(ComposedSourceProjectionAndPredicateUseRevokeRepairPreserveSdkMcpRowsAndReceiptsAcrossTwoColdCuts)";
    private const string OriginalRefusalCompositionFilterArgument = "--KeyLoadTests:Filter=/*/*/(AuthenticationExecutionTests*)|(EventReferenceSelectClusterTests*)|(ComposedProjectionLineageClusterTests*)|(CrossTenantClusterWholeFlowTests)/(RealHttpSdkAndOfficialMcpAuthenticateOncePerRequestThenFreshPolicyRefusalRepairReplayAndTwoColdCuts)|(EventReferenceSelectRepairsPersistedUseAndMissingTargetThenReplaysLiteralSdkMcpRowsAcrossTwoColdCuts)|(GenuineSixOwnerCurrentSourceRightsRefusalRestoresOriginalGraphRequestThenFullReceiptsSurviveTwoColdReopens)|(ComposedSourceProjectionAndPredicateUseRevokeRepairPreserveSdkMcpRowsAndReceiptsAcrossTwoColdCuts)|(ForeignWritesScansAndIndexesAreDeniedAndAuthorizedReceiptRemainsStable)|(SdkWriteHeldAcrossPersistedRevocationIsUnauthorizedWithoutEffects)|(OfficialMcpWriteHeldAcrossPersistedRevocationIsUnauthorizedWithoutEffects)";
    private const string ReleaseEarlyAdmissionQualificationFilterArgument = "--KeyLoadTests:Filter=/*/*/EarlyAdmissionQualificationTests/*";
    private const string ReleaseQueryRuleOptimizationClusterFilterArgument = "--KeyLoadTests:Filter=/*/*/QueryRuleOptimizationClusterTests/*";
    private const string ReleaseStrongReadSessionAbsenceClusterFilterArgument = "--KeyLoadTests:Filter=/*/*/StrongReadSessionAbsenceClusterTests/*";
    private const string ReleaseTimeSeriesChunkQualificationClusterFilterArgument = "--KeyLoadTests:Filter=/*/*/TimeSeriesChunkQualificationClusterTests/*";
    private const string ReleaseCombinedFilterArgument = "--KeyLoadTests:Filter=/*/*/(EarlyAdmissionQualificationTests|QueryRuleOptimizationClusterTests|StrongReadSessionAbsenceClusterTests|TimeSeriesChunkQualificationClusterTests)/*";
    private const string CurrentSafeProjectorFilterArgument = "--KeyLoadTests:Filter=/*/*/SafeProjectorOperationClusterTests/*";
    private const string CurrentSqlParserAdmissionFilterArgument = "--KeyLoadTests:Filter=/*/*/SqlParserAdmissionClusterWholeFlowTests/*";
    private const string CurrentGraphSearchPathPolicyFilterArgument = "--KeyLoadTests:Filter=/*/*/GraphSearchPathPolicyClusterTests/*";
    private const string CurrentSqlGraphPublicFilterArgument = "--KeyLoadTests:Filter=/*/*/(SqlParserAdmissionClusterWholeFlowTests|GraphSearchPathPolicyClusterTests)/*";
    private const string CurrentFoundationClusterFilterArgument = "--KeyLoadTests:Filter=/*/*/(SqlParserAdmissionClusterWholeFlowTests|GraphSearchPathPolicyClusterTests|SagaDueClusterTests|SagaTimeoutClusterTests|DocumentSessionReadClusterTests|StrongReadSessionAbsenceClusterTests)/*";
    private const string CurrentGraphSearchLineageFilterArgument = "--KeyLoadTests:Filter=/*/*/GraphSearchLineageClusterTests/*";
    private const string CurrentAtomicTriadFilterArgument = "--KeyLoadTests:Filter=/*/*/AtomicTriadStagedClusterTests/*";
    private const string CurrentCompetingInboxFilterArgument = "--KeyLoadTests:Filter=/*/*/CompetingInboxPublicTests/*";
    private const string CurrentQueueViewLeaseFilterArgument = "--KeyLoadTests:Filter=/*/*/QueueViewLeaseIsolationTests/*";
    private const string CurrentCombinedFilterArgument = "--KeyLoadTests:Filter=/*/*/(SafeProjectorOperationClusterTests|SqlParserAdmissionClusterWholeFlowTests|GraphSearchLineageClusterTests|AtomicTriadStagedClusterTests|CompetingInboxPublicTests|QueueViewLeaseIsolationTests)/*";
    private const string SlowBodyReturnFilterArgument = "--KeyLoadTests:Filter=/*/*/SlowBodyAdmissionReturnTests/*";
    private const string RowRevisionAdmissionFilterArgument = "--KeyLoadTests:Filter=/*/*/RowRevisionInferenceClusterTests/*";
    private const string FoundationAdmissionRepairFilterArgument = "--KeyLoadTests:Filter=/*/*/(SlowBodyAdmissionReturnTests|RowRevisionInferenceClusterTests)/*";
    private const string CatalogBootstrapAuthorityFilterArgument = "--KeyLoadTests:Filter=/*/*/CatalogBootstrapAuthorityClusterTests/*";
    private const string FoundationAdmissionAuthorityFilterArgument = "--KeyLoadTests:Filter=/*/*/(SlowBodyAdmissionReturnTests|RowRevisionInferenceClusterTests|CatalogBootstrapAuthorityClusterTests)/*";
    private const string FoundationTelemetryFilterArgument = "--KeyLoadTests:Filter=/*/*/FoundationOtlpClusterTests/NativeDashboardReceivesActualClusterSdkMcpPolicyRepairTracesAndPrivateFreeRejectionLogs";
    private const string AspireFaultFilterArgument = "--KeyLoadTests:Filter=/*/*/AspireContainerFaultCommandTests/WrongStaleAndForeignIdsRefuseWithoutEffectsBeforeHealthySdkMcpRestart";
    private const string AcceptedRestartCancellationFilterArgument = "--KeyLoadTests:Filter=/*/*/AggregateReplayClusterSiloRestartTests/AcEvent008AcceptedRestartCancellationResumesSameReplacementAndPreservesReplay";
    private const string FunctionalSingleFilterArgument = "--KeyLoadTests:Filter=/*/*/FunctionalDatabaseGateTests/SingleInstanceRunsEveryModelThroughSdkMcpAndSqlWithPersistedUsers";
    private const string FunctionalClusterFilterArgument = "--KeyLoadTests:Filter=/*/*/FunctionalDatabaseGateTests/ThreeNodeClusterRunsSameEveryModelScenarioThroughSdkMcpAndSql";
    private const string PartitionQueryValidationFilterArgument = "--KeyLoadTests:Filter=/*/*/PartitionQueryPublicValidationTests/*";
    private const string CrossTenantWholeFlowFilterArgument = "--KeyLoadTests:Filter=/*/*/CrossTenantClusterWholeFlowTests/ForeignWritesScansAndIndexesAreDeniedAndAuthorizedReceiptRemainsStable";
    private const string FunctionalSingleColdFilterArgument = "--KeyLoadTests:Filter=/*/*/FunctionalDatabaseColdReopenTests/SingleInstanceColdReopensAllModelsAndReplaysOriginalPublicCommands";
    private const string FunctionalClusterColdFilterArgument = "--KeyLoadTests:Filter=/*/*/FunctionalDatabaseColdReopenTests/ThreeNodeColdReopensAllModelsAndReplaysOriginalPublicCommands";
    private const string FunctionalFileOwnershipFilterArgument = "--KeyLoadTests:Filter=/*/*/FunctionalFileOwnershipClusterTests/DistinctNativeFilesRetainAllModelsAcrossCompetingOwnersAndTwoColdReopens";
    private const string EnabledArgument = "--KeyLoadTests:LocalClusterImage:Enabled=true";
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
            "--KEYLOAD_LOCAL_CLUSTER_IMAGE_CHILD=" + ChildMarker
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
            throw new InvalidOperationException("The local Orleans image selection is invalid.");
        }
        var arguments = JsonSerializer.Deserialize<string[]>(value);
        if (arguments is not [ClusterSuiteArgument, StandardFilterArgument or RejectionFilterArgument or NativeTextFilterArgument
                or RetainedSnapshotFilterArgument or NullQueueDeadlineFilterArgument or GraphIncomingFilterArgument or ResourcePolicyFilterArgument or McpCurrentAuthorityFilterArgument or McpCredentialAuthorityFilterArgument or McpInitialCredentialFilterArgument
                or ProjectionReplayFilterArgument or ProjectionPolicyFilterArgument or ProjectionColdFilterArgument or GraphEventLineageAuthenticationFilterArgument or OriginalRefusalCompositionFilterArgument or PublicModelFlowFilterArgument or ExpandedPublicModelFlowFilterArgument or SuccessorPublicModelFlowFilterArgument or PublicCohortFilterArgument or SuccessorPublicCohortFilterArgument or FinalPublicCohortFilterArgument or ReleaseEarlyAdmissionQualificationFilterArgument or ReleaseQueryRuleOptimizationClusterFilterArgument or ReleaseStrongReadSessionAbsenceClusterFilterArgument or ReleaseTimeSeriesChunkQualificationClusterFilterArgument or ReleaseCombinedFilterArgument
                or CurrentSafeProjectorFilterArgument or CurrentSqlParserAdmissionFilterArgument or CurrentGraphSearchLineageFilterArgument or CurrentAtomicTriadFilterArgument or CurrentCompetingInboxFilterArgument or CurrentQueueViewLeaseFilterArgument or CurrentCombinedFilterArgument
                or SlowBodyReturnFilterArgument or RowRevisionAdmissionFilterArgument
                or FoundationAdmissionRepairFilterArgument or CatalogBootstrapAuthorityFilterArgument
                or FoundationAdmissionAuthorityFilterArgument or FoundationTelemetryFilterArgument
                or AspireFaultFilterArgument or AcceptedRestartCancellationFilterArgument
                or FunctionalSingleFilterArgument or FunctionalClusterFilterArgument or PartitionQueryValidationFilterArgument
                or CrossTenantWholeFlowFilterArgument or FunctionalSingleColdFilterArgument or FunctionalClusterColdFilterArgument
                or FunctionalFileOwnershipFilterArgument
                or CurrentGraphSearchPathPolicyFilterArgument or CurrentSqlGraphPublicFilterArgument or CurrentFoundationClusterFilterArgument, EnabledArgument]
            || Environment.GetEnvironmentVariable(NativeCoverageArgumentsEnvironment) is not null
            || HasAmbientGithubIdentity()
            || Environment.GetEnvironmentVariable(ProvenanceEnvironment) is not null
            || Environment.GetEnvironmentVariable(ReceiptEnvironment) is not null
            || Environment.GetEnvironmentVariable(ReferenceEnvironment) is not null
            || Environment.GetEnvironmentVariable(ChildEnvironment) is not null)
        {
            throw new InvalidOperationException("The local Orleans image selection is invalid.");
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
            throw new InvalidOperationException("The local Orleans image identity is invalid.");
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
            || !LocalClusterImageReferenceValidation.IsCanonicalReferenceAndReceipt(reference, receipt))
        {
            throw new InvalidOperationException("The local Orleans image identity is invalid.");
        }
        var tag = reference[(reference.IndexOf(':', StringComparison.Ordinal) + 1)..];
        return new(reference, tag, receipt, child ?? EnabledValue);
    }

    private static bool HasAmbientGithubIdentity()
        => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(GitHubReceiptEnvironment))
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(GitHubRevisionEnvironment))
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(GitHubActionsEnvironment));

}
