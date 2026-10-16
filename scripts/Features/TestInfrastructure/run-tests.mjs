import { spawn } from 'node:child_process';
import { accessSync, constants, realpathSync, statSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
const defaultMaximumParallelTests = 50;
const maximumParallelTests = 50;
const comparisonParallelTests = 1;
const nativeSimdOptions = ['DOTNET_EnableHWIntrinsic', 'COMPlus_EnableHWIntrinsic',
  'DOTNET_EnableSIMD', 'COMPlus_EnableSIMD'];
const heavyLoadEnabledOption = 'HeavyLoad:Enabled';
const heavyLoadFilter = '/*/*/HeavyDocumentLoadRf3Tests/*';
const ordinaryRf3Filter = '/*/*/*/*[Category!=HeavyLoad]';
const projects = new Map([
  ['analyzers', 'KeyLoad.Analyzers.Tests'], ['unit', 'KeyLoad.UnitTests'],
  ['recovery', 'KeyLoad.RecoveryTests'],
  ['rf3', 'KeyLoad.IntegrationTests'], ['comparison', 'KeyLoad.ComparisonTests'], ['site', 'KeyLoad.SiteTests']
]);
const localImageArgumentsEnvironment = 'KEYLOAD_TUNIT_LOCAL_RF3_IMAGE_ARGUMENTS';
const localImageFilters = new Set(['/*/*/EventProjectionRf3Tests/AcLineage001ExactReplayConflictAndStaleInputPreserveOnlyTheCommittedProjection', '/*/*/EventProjectionRf3Tests/AcLineage002SourceHiddenFromPersistedReaderCannotProduceVisibleDerivedVector', '/*/*/EventProjectionRf3Tests/AcLineage003CommittedProjectionReceiptAndLiveVectorSurviveLeaderLossAndRestart', '/*/*/McpCredentialLifecycleTests/AcMcp002NativeDiscoveryRejectsMissingAndUnconfiguredCredentials', '/*/*/McpPersistedAuthorizationTests/AcMcp002CurrentPersistedPrincipalControlsEachCallAfterDiscovery', '/*/*/McpCredentialLifecycleTests/AcMcp002PersistedCredentialChangesRejectTheClientAfterDiscovery', '/*/*/ResourcePolicyUpdateRf3Tests/AcRpol004PolicyAckFencesReadersCursorsAndStaleRetriedUpdatesAcrossRf3', '/*/*/EventFeedFirstOpenRf3Tests/ActualSixOwnerFirstOpenPinsReplaySourceRevocationRepairAndSameRootCold', '/*/*/GraphIncomingRf3Tests/PersistedSourceGrantHidesCrossRowsAndTargetGraphDenialIsExplicit', '/*/*/NativeSignedPurposeRf3Tests/ForegroundSdkOrOfficialMcpSignedPurposeRefusesThenExactRepairReplayAndTwoColdCuts', '/*/*/NativeCapabilityOmissionRf3Tests/ForegroundSdkOrOfficialMcpOmittedCapabilityRefusesThenSameOwnerRepairAndColdReplay', '/*/*/EmptyReplicaSnapshotRf3Tests/*',
  '/*/*/QueueDeadlineAutonomousRf3Tests/NullSelectedServicePreservesScheduledStateReceiptsAcrossColdThenPublicClaimAndHealthyAck',
  '/*/*/ClusterTests/RetainedReplicaCatchesUpThroughNativeSnapshotAndKeepsCommandOutcomes',
  '/*/*/RemoteTransferDistinctOwnerTests/ActualDistinctOwnersRetainAcceptReceiptAcrossTwoColdRestartsAndBDoesNotResurrectAcknowledgedMessage',
  '/*/*/RemoteAnnPublicationRf3Tests/DifferentVoterSdkAndOfficialMcpObserveActualPublicationAcrossMovePolicyRepairAndTwoColdRoots',
  '/*/*/TwoRf3MembershipProfileTests/*',
  '/*/*/(PartitionQueryMcpSchemaTests|RelationalSqlRf3JoinTests|RelationalSqlRf3JoinAuthorizationTests|RelationalSqlRf3JoinBudgetTests|RelationalSqlRf3JoinCancellationTests|RelationalSqlRf3JoinReadCutTests)/*',
  '/*/*/RelationalSqlRf3JoinRejectionTests/*',
  '/*/*/(ConnectionRf3SequentialTests|ConnectionRf3OverlapTests|ConnectionRf3AuthorizationTests)/*',
  '/*/*/(NativeTextAsyncRf3Tests)|(NativeTextMaintenanceRf3Tests)|(NativeTextRf3LeaderLossTests)|(NativeTextRf3Tests)|(NativeTextWaitRf3Tests)/*',
  '/*/*/(AuthenticationExecutionTests*)|(EventReferenceSelectRf3Tests*)|(NativeRemoteGraphRf3Tests*)|(ComposedProjectionLineageRf3Tests*)/(RealHttpSdkAndOfficialMcpAuthenticateOncePerRequestThenFreshPolicyRefusalRepairReplayAndTwoColdCuts)|(AcKl095EventReferenceSelectRepairsPersistedUseAndMissingTargetThenReplaysLiteralSdkMcpRowsAcrossTwoColdCuts)|(GenuineSixOwnerCurrentSourceRightsRefusalRestoresOriginalGraphRequestThenFullReceiptsSurviveTwoColdReopens)|(AcKl048ComposedSourceProjectionAndPredicateUseRevokeRepairPreserveSdkMcpRowsAndReceiptsAcrossTwoColdCuts)',
  '/*/*/(AuthenticationExecutionTests*)|(EventReferenceSelectRf3Tests*)|(NativeRemoteGraphRf3Tests*)|(ComposedProjectionLineageRf3Tests*)|(CrossTenantRf3WholeFlowTests)|(RequestCqrsAuthorityFaultTests)/(RealHttpSdkAndOfficialMcpAuthenticateOncePerRequestThenFreshPolicyRefusalRepairReplayAndTwoColdCuts)|(AcKl095EventReferenceSelectRepairsPersistedUseAndMissingTargetThenReplaysLiteralSdkMcpRowsAcrossTwoColdCuts)|(GenuineSixOwnerCurrentSourceRightsRefusalRestoresOriginalGraphRequestThenFullReceiptsSurviveTwoColdReopens)|(AcKl048ComposedSourceProjectionAndPredicateUseRevokeRepairPreserveSdkMcpRowsAndReceiptsAcrossTwoColdCuts)|(Kl015ForeignWritesScansAndIndexesAreDeniedAndAuthorizedReceiptRemainsStable)|(SdkWriteHeldAcrossPersistedRevocationIsUnauthorizedWithoutEffects)|(OfficialMcpWriteHeldAcrossPersistedRevocationIsUnauthorizedWithoutEffects)']);
localImageFilters.add('/*/*/(TypedEndpointJoinRf3Tests|DeclarativeCompositionReverseQuotaRf3Tests|SubscriptionPhasePublicColdTests|BlobSupersessionPublicTests|BlobPartialReadBoundaryPublicTests|NativeTextProtectedPublicColdTests|CurrentOwnershipTokenAfterMovementRf3Tests)/*');
localImageFilters.add('/*/*/(TypedEndpointJoinRf3Tests|DeclarativeCompositionReverseQuotaRf3Tests|SubscriptionPhasePublicColdTests|BlobSupersessionPublicTests|BlobPartialReadBoundaryPublicTests|NativeTextProtectedPublicColdTests|CurrentOwnershipTokenAfterMovementRf3Tests|QueueLifecyclePublicRf3Tests|PhysicalShardPartitionMovementRf3Tests|TenantPackingActivationRf3Tests|BlobBoundedRetirementPublicTests|BlobPublicCancellationTests|BlobReadPolicyPublicTests|BlobReservationQuotaPublicTests|NativeTextNestedWildcardPublicTests|NativeTextRawReadPolicyPublicTests|NativeTextRequestRefusalPublicTests|NativeTextRowOwnershipPublicTests)/*');
const nativeTextSdkUid = "KeyLoad.IntegrationTests.Features.Search.NativeTextMaintenanceRf3Tests(KeyLoad.IntegrationTests.ClusterFixture).1.1.ProtectedNativeTextMaintenanceReplaysBilingualUpdateDeleteAcrossAllPublicPaths(KeyLoad.IntegrationTests.Features.Search.NativeTextMaintenancePath).1.1.0";
localImageFilters.add('/*/*/(TypedEndpointJoinRf3Tests|DeclarativeCompositionReverseQuotaRf3Tests|SubscriptionPhasePublicColdTests|BlobSupersessionPublicTests|BlobPartialReadBoundaryPublicTests|NativeTextProtectedPublicColdTests|CurrentOwnershipTokenAfterMovementRf3Tests|QueueLifecyclePublicRf3Tests|PhysicalShardPartitionMovementRf3Tests|TenantPackingActivationRf3Tests|BlobBoundedRetirementPublicTests|BlobPublicCancellationTests|BlobReadPolicyPublicTests|BlobReservationQuotaPublicTests|NativeTextNestedWildcardPublicTests|NativeTextRawReadPolicyPublicTests|NativeTextRequestRefusalPublicTests|NativeTextRowOwnershipPublicTests|GraphVertexPublicWholeFlowTests|QueueLeaseFencingRf3Tests|PartitionMovementFencedAbortRf3Tests|TenantDedicatedOwnerRf3Tests|PostgresNativeSessionRf3Tests|BlobRowCreatorPublicTests|BlobRejectedContentPublicTests|BlobListRowPolicyPublicTests|NativeTextNormalizationPublicTests|NativeTextDistinctTermsPublicTests)/*');
localImageFilters.add('/*/*/(BlobArchiveActivePublicTests|BlobBoundedRetirementPublicTests|BlobListRowPolicyPublicTests|BlobPartialReadBoundaryPublicTests|BlobPublicCancellationTests|BlobReadPolicyPublicTests|BlobRejectedContentPublicTests|BlobReservationQuotaPublicTests|BlobRestoredReclaimPublicTests|BlobRowCreatorPublicTests|BlobSupersessionPublicTests|CurrentOwnershipTokenAfterMovementRf3Tests|DeclarativeCompositionReverseQuotaRf3Tests|GraphVertexPublicWholeFlowTests|NativeTextDistinctTermsPublicTests|NativeTextNestedWildcardPublicTests|NativeTextNormalizationPublicTests|NativeTextProtectedPublicColdTests|NativeTextRawReadPolicyPublicTests|NativeTextRequestRefusalPublicTests|NativeTextRowOwnershipPublicTests|NativeTextSelectedSchemaPublicTests|PartitionMovementFencedAbortRf3Tests|PhysicalShardPartitionMovementRf3Tests|PostgresNativeSessionRf3Tests|PostgresPortalOwnershipRf3Tests|PostgresPositiveExecuteRf3Tests|ProjectionPublicColdTests|QueueLeaseFencingRf3Tests|QueueLifecyclePublicRf3Tests|SqlFetchCountRf3WholeFlowTests|SqlNullOrderingRf3WholeFlowTests|SqlOffsetRf3WholeFlowTests|SubscriptionPhasePublicColdTests|TenantCapacityPlacementRf3Tests|TenantDedicatedOwnerRf3Tests|TenantPackingActivationRf3Tests|TypedEndpointJoinRf3Tests)/*');
localImageFilters.add("/*/*/(BlobActiveUploadSlotPublicTests|BlobArchiveActivePublicTests|BlobBoundedRetirementPublicTests|BlobCompleteCasPublicTests|BlobEmptySlotPublicTests|BlobListRowPolicyPublicTests|BlobObjectKeyLifetimePublicTests|BlobPartialReadBoundaryPublicTests|BlobPublicCancellationTests|BlobReadPolicyPublicTests|BlobRejectedContentPublicTests|BlobReservationQuotaPublicTests|BlobRestoredReclaimPublicTests|BlobRowCreatorPublicTests|BlobScopedUploadIdentityPublicTests|BlobSupersessionPublicTests|CurrentOwnershipTokenAfterMovementRf3Tests|DeclarativeCompositionReverseQuotaRf3Tests|GraphVertexPublicWholeFlowTests|NativeTextDistinctTermsPublicTests|NativeTextEmptyMaintainedPublicTests|NativeTextFreshSchemaGenerationPublicTests|NativeTextNestedWildcardPublicTests|NativeTextNonStringTransitionsPublicTests|NativeTextNormalizationPublicTests|NativeTextPartitionGenerationPublicTests|NativeTextProtectedPublicColdTests|NativeTextRawReadPolicyPublicTests|NativeTextRequestRefusalPublicTests|NativeTextRowOwnershipPublicTests|NativeTextSelectedSchemaPublicTests|PartitionMovementFencedAbortRf3Tests|PhysicalShardPartitionMovementRf3Tests|PostgresDecimalRf3WholeFlowTests|PostgresErrorReplyReceiverRf3Tests|PostgresInt16Rf3WholeFlowTests|PostgresNativeSessionRf3Tests|PostgresPortalOwnershipRf3Tests|PostgresPositiveExecuteRf3Tests|ProjectionPublicColdTests|QueueLeaseFencingRf3Tests|QueueLifecyclePublicRf3Tests|SqlFetchCountRf3WholeFlowTests|SqlNullDistinctRf3WholeFlowTests|SqlNullOrderingRf3WholeFlowTests|SqlOffsetRf3WholeFlowTests|SubscriptionPhasePublicColdTests|TenantCapacityPlacementRf3Tests|TenantDedicatedOwnerRf3Tests|TenantPackingActivationRf3Tests|TypedEndpointJoinRf3Tests)/*");
localImageFilters.add("/*/*/(BlobActiveUploadSlotPublicTests|BlobArchiveActivePublicTests|BlobBoundedRetirementPublicTests|BlobCompleteCasPublicTests|BlobEmptySlotPublicTests|BlobListRowPolicyPublicTests|BlobObjectKeyLifetimePublicTests|BlobPartialReadBoundaryPublicTests|BlobPublicCancellationTests|BlobReadPolicyPublicTests|BlobRejectedContentPublicTests|BlobReservationQuotaPublicTests|BlobRestoredReclaimPublicTests|BlobRowCreatorPublicTests|BlobScopedUploadIdentityPublicTests|BlobSupersessionPublicTests|CurrentOwnershipTokenAfterMovementRf3Tests|DeclarativeCompositionReverseQuotaRf3Tests|GraphVertexPublicWholeFlowTests|NativeTextDistinctTermsPublicTests|NativeTextEmptyMaintainedPublicTests|NativeTextFreshSchemaGenerationPublicTests|NativeTextNestedWildcardPublicTests|NativeTextNonStringTransitionsPublicTests|NativeTextNormalizationPublicTests|NativeTextPartitionGenerationPublicTests|NativeTextProtectedPublicColdTests|NativeTextRawReadPolicyPublicTests|NativeTextRequestRefusalPublicTests|NativeTextRowOwnershipPublicTests|NativeTextSelectedSchemaPublicTests|PartitionMovementFencedAbortRf3Tests|PhysicalShardPartitionMovementRf3Tests|PostgresDecimalRf3WholeFlowTests|PostgresErrorReplyReceiverRf3Tests|PostgresNativeSessionRf3Tests|PostgresPortalOwnershipRf3Tests|PostgresPositiveExecuteRf3Tests|ProjectionPublicColdTests|QueueLeaseFencingRf3Tests|QueueLifecyclePublicRf3Tests|SqlFetchCountRf3WholeFlowTests|SqlNullDistinctRf3WholeFlowTests|SqlNullOrderingRf3WholeFlowTests|SqlOffsetRf3WholeFlowTests|SubscriptionPhasePublicColdTests|TenantCapacityPlacementRf3Tests|TenantDedicatedOwnerRf3Tests|TenantPackingActivationRf3Tests|TypedEndpointJoinRf3Tests)/*");
localImageFilters.add("/*/*/(BlobMaintenancePolicyPublicTests|BlobMaximumRangePublicTests|BlobTombstoneListingPublicTests|NativeTextReleasedRepairPublicTests|NativeTextSelectedExplainPublicTests|NativeTextSelectedFilterPublicTests|SdkProblemBudgetPublicTests|TimeSeriesAggregateScanBoundRf3Tests|TimeSeriesCurrentCapabilityRf3Tests)/*");
localImageFilters.add("/*/*/(BlobActiveUploadSlotPublicTests|BlobArchiveActivePublicTests|BlobBoundedRetirementPublicTests|BlobCompleteCasPublicTests|BlobEmptySlotPublicTests|BlobListRowPolicyPublicTests|BlobMaintenancePolicyPublicTests|BlobMaximumRangePublicTests|BlobObjectKeyLifetimePublicTests|BlobPartialReadBoundaryPublicTests|BlobPublicCancellationTests|BlobReadPolicyPublicTests|BlobRejectedContentPublicTests|BlobReservationQuotaPublicTests|BlobRestoredReclaimPublicTests|BlobRowCreatorPublicTests|BlobScopedUploadIdentityPublicTests|BlobSupersessionPublicTests|BlobTombstoneListingPublicTests|CurrentOwnershipTokenAfterMovementRf3Tests|DeclarativeCompositionReverseQuotaRf3Tests|GraphVertexPublicWholeFlowTests|NativeTextDistinctTermsPublicTests|NativeTextEmptyMaintainedPublicTests|NativeTextFreshSchemaGenerationPublicTests|NativeTextNestedWildcardPublicTests|NativeTextNonStringTransitionsPublicTests|NativeTextNormalizationPublicTests|NativeTextPartitionGenerationPublicTests|NativeTextProtectedPublicColdTests|NativeTextRawReadPolicyPublicTests|NativeTextReleasedRepairPublicTests|NativeTextRequestRefusalPublicTests|NativeTextRowOwnershipPublicTests|NativeTextSelectedExplainPublicTests|NativeTextSelectedFilterPublicTests|NativeTextSelectedSchemaPublicTests|PartitionMovementFencedAbortRf3Tests|PhysicalShardPartitionMovementRf3Tests|PostgresDecimalRf3WholeFlowTests|PostgresErrorReplyReceiverRf3Tests|PostgresInt16Rf3WholeFlowTests|PostgresNativeSessionRf3Tests|PostgresPortalOwnershipRf3Tests|PostgresPositiveExecuteRf3Tests|ProjectionPublicColdTests|QueueLeaseFencingRf3Tests|QueueLifecyclePublicRf3Tests|SdkProblemBudgetPublicTests|SqlFetchCountRf3WholeFlowTests|SqlNullDistinctRf3WholeFlowTests|SqlNullOrderingRf3WholeFlowTests|SqlOffsetRf3WholeFlowTests|SubscriptionPhasePublicColdTests|TenantCapacityPlacementRf3Tests|TenantDedicatedOwnerRf3Tests|TenantPackingActivationRf3Tests|TimeSeriesAggregateScanBoundRf3Tests|TimeSeriesCurrentCapabilityRf3Tests|TypedEndpointJoinRf3Tests)/*");
localImageFilters.add("/*/*/DistributedQueryAuthorityRf3Tests/*");
localImageFilters.add("/*/*/EarlyAdmissionQualificationTests/*");
localImageFilters.add("/*/*/MetadataBootstrapRf3Tests/*");
localImageFilters.add("/*/*/QueryRuleOptimizationRf3Tests/*");
localImageFilters.add("/*/*/StrongReadSessionAbsenceRf3Tests/*");
localImageFilters.add("/*/*/TimeSeriesChunkQualificationRf3Tests/*");
localImageFilters.add("/*/*/(DistributedQueryAuthorityRf3Tests|EarlyAdmissionQualificationTests|MetadataBootstrapRf3Tests|QueryRuleOptimizationRf3Tests|StrongReadSessionAbsenceRf3Tests|TimeSeriesChunkQualificationRf3Tests)/*");
localImageFilters.add("/*/*/SafeProjectorOperationRf3Tests/*");
localImageFilters.add("/*/*/SqlParserAdmissionRf3WholeFlowTests/*");
localImageFilters.add("/*/*/GraphSearchPathPolicyRf3Tests/*");
localImageFilters.add("/*/*/SampleChunkCorrectionPredicateRf3Tests/*");
localImageFilters.add("/*/*/(SqlParserAdmissionRf3WholeFlowTests|GraphSearchPathPolicyRf3Tests)/*");
localImageFilters.add("/*/*/(SqlParserAdmissionRf3WholeFlowTests|GraphSearchPathPolicyRf3Tests|SampleChunkCorrectionPredicateRf3Tests)/*");
localImageFilters.add("/*/*/(GraphSearchPathPolicyRf3Tests|SampleChunkCorrectionPredicateRf3Tests)/*");
localImageFilters.add("/*/*/(NativeDatabaseMigrationRf3Tests|SqlParserAdmissionRf3WholeFlowTests|GraphSearchPathPolicyRf3Tests|RequestCqrsPhaseFaultTests|RequestCqrsAuthorityFaultTests|RequestCqrsRf3FollowerRestartTests|SagaDueRf3Tests|SagaTimeoutRf3Tests|SampleChunkCorrectionPredicateRf3Tests|ConnectionRf3AuthorizationTests|DocumentSessionReadRf3Tests|MetadataBootstrapRf3Tests|PartitionMovementPublicParentFlowTests|PhysicalOwnerRegistrationRf3Tests|ProtectedDocumentRf3Tests|QueryContextSessionRf3Tests|StrongReadSessionAbsenceRf3Tests)/*");
localImageFilters.add("/*/*/GraphSearchLineageRf3Tests/*");
localImageFilters.add("/*/*/AtomicTriadStagedRf3Tests/*");
localImageFilters.add("/*/*/CompetingInboxPublicTests/*");
localImageFilters.add("/*/*/QueueViewLeaseIsolationTests/*");
localImageFilters.add("/*/*/(SafeProjectorOperationRf3Tests|SqlParserAdmissionRf3WholeFlowTests|GraphSearchLineageRf3Tests|AtomicTriadStagedRf3Tests|CompetingInboxPublicTests|QueueViewLeaseIsolationTests)/*");
localImageFilters.add('/*/*/NativeDatabaseMigrationRf3Tests/*');
localImageFilters.add('/*/*/(SafeProjectorOperationRf3Tests|SqlParserAdmissionRf3WholeFlowTests|GraphSearchLineageRf3Tests|AtomicTriadStagedRf3Tests|CompetingInboxPublicTests|QueueViewLeaseIsolationTests|NativeDatabaseMigrationRf3Tests)/*');
localImageFilters.add("/*/*/SlowBodyAdmissionReturnTests/*");
localImageFilters.add("/*/*/RowRevisionInferenceRf3Tests/*");
localImageFilters.add("/*/*/(NativeDatabaseMigrationRf3Tests|SlowBodyAdmissionReturnTests|RowRevisionInferenceRf3Tests)/*");
localImageFilters.add("/*/*/CatalogBootstrapAuthorityRf3Tests/*");
localImageFilters.add("/*/*/(SlowBodyAdmissionReturnTests|RowRevisionInferenceRf3Tests|CatalogBootstrapAuthorityRf3Tests)/*");
localImageFilters.add("/*/*/FoundationOtlpRf3Tests/NativeDashboardReceivesActualRf3SdkMcpPolicyRepairTracesAndPrivateFreeRejectionLogs");
localImageFilters.add("/*/*/AspireContainerFaultCommandTests/WrongStaleAndForeignIdsRefuseWithoutEffectsBeforeHealthySdkMcpRestart");
localImageFilters.add("/*/*/AggregateReplayRf3LeaderLossTests/AcEvent008AcceptedRestartCancellationResumesSameReplacementAndPreservesReplay");
localImageFilters.add("/*/*/ControlledPartitionMovementNativeRetireTests/OriginalNativeRetireCallerCancellationAndHostStopRetainAuthorityThenResumeReplayMixedModelsAndTwoColdOwners");
localImageFilters.add("/*/*/FunctionalDatabaseGateTests/SingleInstanceRunsEveryModelThroughSdkMcpAndSqlWithPersistedUsers");
localImageFilters.add("/*/*/FunctionalDatabaseGateTests/ThreeNodeClusterRunsSameEveryModelScenarioThroughSdkMcpAndSql");
const nativeTextPreparedFilter = '/*/*/(NativeTextAsyncRf3Tests)|(NativeTextMaintenanceRf3Tests)|(NativeTextRf3LeaderLossTests)|(NativeTextRf3Tests)|(NativeTextWaitRf3Tests)/*';
const localImageGithubIdentity = ['KEYLOAD_IMAGE_RECEIPT', 'GITHUB_SHA', 'GITHUB_ACTIONS'];
const localImageInheritedIdentity = ['KEYLOAD_IMAGE_PROVENANCE', 'KEYLOAD_LOCAL_IMAGE_RECEIPT',
  'KeyLoad__ContainerImages__Server', 'KEYLOAD_LOCAL_RF3_IMAGE_CHILD',
  'KEYLOAD_TUNIT_NATIVE_COVERAGE_ARGUMENTS', localImageArgumentsEnvironment];
const mapped = new Map([
  ['Filter', '--treenode-filter'], ['ResultsDirectory', '--results-directory'],
  ['CoverageSettings', '--coverage-settings'], ['CoverageOutput', '--coverage-output'],
  ['CoverageFormat', '--coverage-output-format'], ['Execution:MaximumParallelTests', '--maximum-parallel-tests']
]);

// Selection only: workloads and Aspire resource lifetimes belong to the C# TUnit tests.
export function nativeSelection(input, inherited = process.env) {
  if (nativeSimdOptions.some(key => inherited[key] !== undefined && inherited[key] !== '' && inherited[key] !== '1')) {
    throw new Error('Native SIMD must remain enabled.');
  }
  const values = new Map();
  const workload = [];
  for (const argument of input) {
    if (argument.startsWith('--Benchmarks:')) { workload.push(argument); continue; }
    const match = /^--KeyLoadTests:([^=]+)=(.+)$/u.exec(argument);
    if (!match || values.has(match[1])) throw new Error('Invalid or duplicate native test selection.');
    if (match[2].length > 4096) throw new Error('Native test argument exceeds its bound.');
    values.set(match[1], match[2]);
  }
  const suite = values.get('Suite');
  const project = projects.get(suite);
  if (!project) throw new Error('Unsupported native TUnit suite.');
  const heavyLoad = values.has(heavyLoadEnabledOption);
  if (!heavyLoad && values.get('Filter') === heavyLoadFilter) {
    throw new Error('Heavy RF3 load requires its explicit enabled selection.');
  }
  if (heavyLoad && (values.get(heavyLoadEnabledOption) !== 'true' || suite !== 'rf3'
    || values.get('Filter') !== heavyLoadFilter || values.has('CoverageSettings')
    || [...values.keys()].some(key => key.startsWith('NativeCoverage:')))) {
    throw new Error('Heavy RF3 load requires its exact exclusive functional selection.');
  }
  const parallel = Number(values.get('Execution:MaximumParallelTests')
    ?? (suite === 'comparison' || heavyLoad ? comparisonParallelTests : defaultMaximumParallelTests));
  if (!Number.isInteger(parallel) || parallel < 1 || parallel > maximumParallelTests) throw new Error('Invalid native parallelism.');
  if ((suite === 'comparison' || heavyLoad) && parallel !== comparisonParallelTests) {
    throw new Error('Comparison measurements and heavy load require exactly one native test at a time.');
  }
  const localImageEnabled = values.get('LocalRf3Image:Enabled');
  const localImageSelected = values.has('LocalRf3Image:Enabled');
  const filterUid = values.get('FilterUid');
  if (values.has('FilterUid') && (!localImageSelected || localImageEnabled !== 'true'
    || suite !== 'rf3' || values.get('Filter') !== nativeTextPreparedFilter || filterUid !== nativeTextSdkUid)) {
    throw new Error('Invalid native focused text UID selection.');
  }
  if (localImageSelected && (localImageEnabled !== 'true' || suite !== 'rf3'
    || !localImageFilters.has(values.get('Filter'))
    || [...values.keys()].some(key => !['Suite', 'Filter', 'FilterUid', 'LocalRf3Image:Enabled',
      'ReportTrx', 'TimeoutMinutes', 'ResultsDirectory', 'Execution:MaximumParallelTests'].includes(key))
    || localImageGithubIdentity.some(key => inherited[key] !== undefined && inherited[key] !== '')
    || localImageInheritedIdentity.some(key => inherited[key] !== undefined && inherited[key] !== ''))) {
    throw new Error('Invalid native local RF3 image selection.');
  }
  if (suite === 'rf3' && !values.has('Filter')) values.set('Filter', ordinaryRf3Filter);
  const environment = { ...inherited };
  if (process.platform === 'darwin') {
    // Storage roots reject symlink ancestors; MTP pipes need a short existing Unix base.
    for (const [key, fallback] of [['TMPDIR', '/private/tmp'],
      ['TESTINGPLATFORM_PIPE_DIRECTORY', '/private/tmp']]) {
      try {
        const selected = environment[key] || fallback;
        if (!path.isAbsolute(selected)) throw new Error('Relative directory.');
        const physical = realpathSync.native(selected);
        if (!statSync(physical).isDirectory()) throw new Error('Not a directory.');
        accessSync(physical, constants.R_OK | constants.W_OK | constants.X_OK);
        environment[key] = physical;
      } catch {
        throw new Error(`Invalid native macOS ${key} directory.`);
      }
    }
  }
  for (const key of ['KeyLoadTests__Suite', 'KeyLoadTests__ScaleProfile', 'KeyLoadTests__VectorProfile',
    'KeyLoadTests__OpenLoopRate', 'KeyLoadTests__LocalRf3Image__Enabled', 'KeyLoadTests__HeavyLoad__Enabled',
    'KEYLOAD_TUNIT_NATIVE_COVERAGE_ARGUMENTS',
    localImageArgumentsEnvironment]) delete environment[key];
  if (localImageSelected) {
    environment[localImageArgumentsEnvironment] = JSON.stringify([
      '--KeyLoadTests:Suite=' + suite, '--KeyLoadTests:Filter=' + values.get('Filter'),
      '--KeyLoadTests:LocalRf3Image:Enabled=true'
    ]);
  }
  const args = ['test', '--project', `tests/${project}`, '--no-build', '--no-restore', '--configuration', 'Release',
    '--output', 'Detailed', '--github-reporter-style', 'full', '--maximum-parallel-tests', String(parallel),
    '--results-directory', path.resolve(root, values.get('ResultsDirectory') ?? `TestResults/${suite}`)];
  args.push('--timeout', `${values.get('TimeoutMinutes') ?? (['rf3', 'comparison'].includes(suite) ? 60 : 30)}m`);
  const preparation = [];
  for (const [key, value] of values) {
    if (['Suite', 'ResultsDirectory', 'Execution:MaximumParallelTests'].includes(key)) continue;
    if (key === 'ReportTrx') {
      if (!['true', 'false'].includes(value)) throw new Error('Invalid TRX selection.');
      if (value === 'true') args.push('--report-trx');
    } else if (key === 'TimeoutMinutes') {
      const minutes = Number(value);
      if (!Number.isInteger(minutes) || minutes < 1 || minutes > 180) throw new Error('Invalid native timeout.');

    } else if (key === 'FilterUid') {
      args.push('--filter-uid', value, '--minimum-expected-tests', '1');
    } else if (key === 'Filter' && filterUid !== undefined) {
      continue;
    } else if (mapped.has(key)) {
      args.push(mapped.get(key), key.startsWith('Coverage') && key !== 'CoverageFormat' ? path.resolve(root, value) : value);
    } else if (['ScaleProfile', 'VectorProfile', 'OpenLoopRate'].includes(key)) {
      if (suite !== 'comparison') throw new Error('Benchmark selection requires ComparisonTests.');
      environment[key === 'OpenLoopRate' ? 'Benchmarks__OpenLoopRate' : `Benchmarks__${key}`] = value;
    } else if (key === heavyLoadEnabledOption) {
      environment.KeyLoadTests__HeavyLoad__Enabled = value;
    } else if (key === 'LocalRf3Image:Enabled') {
      continue;
    } else if (key.startsWith('NativeCoverage:')) {
      if (suite !== 'rf3') throw new Error('Native RF3 preparation requires IntegrationTests.');
      preparation.push(`--KeyLoadTests:${key}=${value}`);
    } else throw new Error('Unsupported native test argument.');
  }
  const coverageSettings = values.has('CoverageSettings');
  if (coverageSettings !== values.has('CoverageOutput')) throw new Error('Coverage settings and output must be selected together.');
  if (coverageSettings) {
    args.push('--coverage');
    if (!values.has('CoverageFormat')) args.push('--coverage-output-format', 'cobertura');
  }
  if (preparation.length) {
    for (const key of ['Suite', 'Filter', 'TimeoutMinutes', 'ResultsDirectory', 'CoverageSettings', 'CoverageOutput', 'CoverageFormat', 'ReportTrx']) {
      if (values.has(key)) preparation.push(`--KeyLoadTests:${key}=${values.get(key)}`);
    }
    environment.KEYLOAD_TUNIT_NATIVE_COVERAGE_ARGUMENTS = JSON.stringify(preparation);
  }
  for (const argument of workload) {
    if (suite !== 'comparison') throw new Error('Workload settings require ComparisonTests.');
    const match = /^--Benchmarks:([^=]+)=(.+)$/u.exec(argument);
    if (!match) throw new Error('Invalid workload setting.');
    environment[`Benchmarks__${match[1].replaceAll(':', '__')}`] = match[2];
  }
  return { args, environment };
}

export async function runTests(input) {
  const selection = nativeSelection(input);
  const child = spawn('dotnet', selection.args, { cwd: root, env: selection.environment, stdio: 'inherit' });
  const interrupt = () => child.kill('SIGINT');
  const terminate = () => child.kill('SIGTERM');
  process.on('SIGINT', interrupt);
  process.on('SIGTERM', terminate);
  try {
    return await new Promise((resolve, reject) => {
      child.once('error', reject);
      child.once('close', (code, signal) => resolve(code ?? (signal === 'SIGINT' ? 130 : signal === 'SIGTERM' ? 143 : 1)));
    });
  } finally {
    process.off('SIGINT', interrupt);
    process.off('SIGTERM', terminate);
  }
}

const evaluated = process.execArgv.some(argument => argument === '--eval' || argument === '-e');
if (!evaluated && process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  runTests(process.argv.slice(2)).then(code => { process.exitCode = code; }).catch(error => {
    process.stderr.write(`${error.message}\n`);
    process.exitCode = 1;
  });
}
