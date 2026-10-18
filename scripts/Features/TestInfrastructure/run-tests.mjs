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
const heavyLoadFilter = '/*/*/HeavyDocumentLoadClusterTests/*';
const ordinaryClusterFilter = '/*/*/*/*[Category!=HeavyLoad]';
const projects = new Map([
  ['analyzers', 'KeyLoad.Analyzers.Tests'], ['unit', 'KeyLoad.UnitTests'],
  ['recovery', 'KeyLoad.RecoveryTests'],
  ['cluster', 'KeyLoad.IntegrationTests'], ['comparison', 'KeyLoad.ComparisonTests'], ['site', 'KeyLoad.SiteTests']
]);
const localImageArgumentsEnvironment = 'KEYLOAD_TUNIT_LOCAL_CLUSTER_IMAGE_ARGUMENTS';
const localImageFilters = new Set(['/*/*/EventProjectionClusterTests/AcLineage001ExactReplayConflictAndStaleInputPreserveOnlyTheCommittedProjection',
  '/*/*/EventProjectionClusterTests/AcLineage002SourceHiddenFromPersistedReaderCannotProduceVisibleDerivedVector',
  '/*/*/EventProjectionClusterTests/AcLineage003CommittedProjectionReceiptAndLiveVectorSurviveLeaderLossAndRestart',
  '/*/*/McpCredentialLifecycleTests/AcMcp002NativeDiscoveryRejectsMissingAndUnconfiguredCredentials',
  '/*/*/McpPersistedAuthorizationTests/AcMcp002CurrentPersistedPrincipalControlsEachCallAfterDiscovery',
  '/*/*/McpCredentialLifecycleTests/AcMcp002PersistedCredentialChangesRejectTheClientAfterDiscovery',
  '/*/*/ResourcePolicyUpdateClusterTests/AcRpol004PolicyAckFencesReadersCursorsAndStaleRetriedUpdatesAcrossCluster',
  '/*/*/GraphIncomingClusterTests/PersistedSourceGrantHidesCrossRowsAndTargetGraphDenialIsExplicit',
  '/*/*/QueueDeadlineAutonomousClusterTests/NullSelectedServicePreservesScheduledStateReceiptsAcrossColdThenPublicClaimAndHealthyAck',
  '/*/*/ClusterTests/RetainedReplicaCatchesUpThroughNativeSnapshotAndKeepsCommandOutcomes',
  '/*/*/(PartitionQueryMcpSchemaTests|RelationalSqlClusterJoinTests|RelationalSqlClusterJoinAuthorizationTests|RelationalSqlClusterJoinBudgetTests|RelationalSqlClusterJoinCancellationTests|RelationalSqlClusterJoinReadCutTests)/*',
  '/*/*/RelationalSqlClusterJoinRejectionTests/*',
  '/*/*/(NativeTextAsyncClusterTests)|(NativeTextClusterLeaderLossTests)|(NativeTextClusterTests)|(NativeTextWaitClusterTests)/*',
  '/*/*/(AuthenticationExecutionTests*)|(EventReferenceSelectClusterTests*)|(ComposedProjectionLineageClusterTests*)/(RealHttpSdkAndOfficialMcpAuthenticateOncePerRequestThenFreshPolicyRefusalRepairReplayAndTwoColdCuts)|(EventReferenceSelectRepairsPersistedUseAndMissingTargetThenReplaysLiteralSdkMcpRowsAcrossTwoColdCuts)|(GenuineSixOwnerCurrentSourceRightsRefusalRestoresOriginalGraphRequestThenFullReceiptsSurviveTwoColdReopens)|(ComposedSourceProjectionAndPredicateUseRevokeRepairPreserveSdkMcpRowsAndReceiptsAcrossTwoColdCuts)',
  '/*/*/(AuthenticationExecutionTests*)|(EventReferenceSelectClusterTests*)|(ComposedProjectionLineageClusterTests*)|(CrossTenantClusterWholeFlowTests)/(RealHttpSdkAndOfficialMcpAuthenticateOncePerRequestThenFreshPolicyRefusalRepairReplayAndTwoColdCuts)|(EventReferenceSelectRepairsPersistedUseAndMissingTargetThenReplaysLiteralSdkMcpRowsAcrossTwoColdCuts)|(GenuineSixOwnerCurrentSourceRightsRefusalRestoresOriginalGraphRequestThenFullReceiptsSurviveTwoColdReopens)|(ComposedSourceProjectionAndPredicateUseRevokeRepairPreserveSdkMcpRowsAndReceiptsAcrossTwoColdCuts)|(ForeignWritesScansAndIndexesAreDeniedAndAuthorizedReceiptRemainsStable)|(SdkWriteHeldAcrossPersistedRevocationIsUnauthorizedWithoutEffects)|(OfficialMcpWriteHeldAcrossPersistedRevocationIsUnauthorizedWithoutEffects)']);
localImageFilters.add('/*/*/(TypedEndpointJoinClusterTests|DeclarativeCompositionReverseQuotaClusterTests|SubscriptionPhasePublicColdTests|BlobSupersessionPublicTests|BlobPartialReadBoundaryPublicTests|NativeTextProtectedPublicColdTests)/*');
localImageFilters.add('/*/*/(TypedEndpointJoinClusterTests|DeclarativeCompositionReverseQuotaClusterTests|SubscriptionPhasePublicColdTests|BlobSupersessionPublicTests|BlobPartialReadBoundaryPublicTests|NativeTextProtectedPublicColdTests|QueueLifecyclePublicClusterTests|BlobBoundedRetirementPublicTests|BlobReadPolicyPublicTests|BlobReservationQuotaPublicTests|NativeTextNestedWildcardPublicTests|NativeTextRawReadPolicyPublicTests|NativeTextRequestRefusalPublicTests|NativeTextRowOwnershipPublicTests)/*');
localImageFilters.add('/*/*/(TypedEndpointJoinClusterTests|DeclarativeCompositionReverseQuotaClusterTests|SubscriptionPhasePublicColdTests|BlobSupersessionPublicTests|BlobPartialReadBoundaryPublicTests|NativeTextProtectedPublicColdTests|QueueLifecyclePublicClusterTests|BlobBoundedRetirementPublicTests|BlobReadPolicyPublicTests|BlobReservationQuotaPublicTests|NativeTextNestedWildcardPublicTests|NativeTextRawReadPolicyPublicTests|NativeTextRequestRefusalPublicTests|NativeTextRowOwnershipPublicTests|GraphVertexPublicWholeFlowTests|QueueLeaseFencingClusterTests|PostgresNativeSessionClusterTests|BlobRowCreatorPublicTests|BlobRejectedContentPublicTests|BlobListRowPolicyPublicTests|NativeTextNormalizationPublicTests|NativeTextDistinctTermsPublicTests)/*');
localImageFilters.add('/*/*/(BlobBoundedRetirementPublicTests|BlobListRowPolicyPublicTests|BlobPartialReadBoundaryPublicTests|BlobReadPolicyPublicTests|BlobRejectedContentPublicTests|BlobReservationQuotaPublicTests|BlobRowCreatorPublicTests|BlobSupersessionPublicTests|DeclarativeCompositionReverseQuotaClusterTests|GraphVertexPublicWholeFlowTests|NativeTextDistinctTermsPublicTests|NativeTextNestedWildcardPublicTests|NativeTextNormalizationPublicTests|NativeTextProtectedPublicColdTests|NativeTextRawReadPolicyPublicTests|NativeTextRequestRefusalPublicTests|NativeTextRowOwnershipPublicTests|PostgresNativeSessionClusterTests|PostgresPortalOwnershipClusterTests|PostgresPositiveExecuteClusterTests|ProjectionPublicColdTests|QueueLeaseFencingClusterTests|QueueLifecyclePublicClusterTests|SqlFetchCountClusterWholeFlowTests|SqlNullOrderingClusterWholeFlowTests|SqlOffsetClusterWholeFlowTests|SubscriptionPhasePublicColdTests|TypedEndpointJoinClusterTests)/*');
localImageFilters.add("/*/*/(BlobActiveUploadSlotPublicTests|BlobBoundedRetirementPublicTests|BlobCompleteCasPublicTests|BlobEmptySlotPublicTests|BlobListRowPolicyPublicTests|BlobObjectKeyLifetimePublicTests|BlobPartialReadBoundaryPublicTests|BlobReadPolicyPublicTests|BlobRejectedContentPublicTests|BlobReservationQuotaPublicTests|BlobRowCreatorPublicTests|BlobScopedUploadIdentityPublicTests|BlobSupersessionPublicTests|DeclarativeCompositionReverseQuotaClusterTests|GraphVertexPublicWholeFlowTests|NativeTextDistinctTermsPublicTests|NativeTextNestedWildcardPublicTests|NativeTextNonStringTransitionsPublicTests|NativeTextNormalizationPublicTests|NativeTextProtectedPublicColdTests|NativeTextRawReadPolicyPublicTests|NativeTextRequestRefusalPublicTests|NativeTextRowOwnershipPublicTests|PostgresDecimalClusterWholeFlowTests|PostgresErrorReplyReceiverClusterTests|PostgresInt16ClusterWholeFlowTests|PostgresNativeSessionClusterTests|PostgresPortalOwnershipClusterTests|PostgresPositiveExecuteClusterTests|ProjectionPublicColdTests|QueueLeaseFencingClusterTests|QueueLifecyclePublicClusterTests|SqlFetchCountClusterWholeFlowTests|SqlNullDistinctClusterWholeFlowTests|SqlNullOrderingClusterWholeFlowTests|SqlOffsetClusterWholeFlowTests|SubscriptionPhasePublicColdTests|TypedEndpointJoinClusterTests)/*");
localImageFilters.add("/*/*/(BlobActiveUploadSlotPublicTests|BlobBoundedRetirementPublicTests|BlobCompleteCasPublicTests|BlobEmptySlotPublicTests|BlobListRowPolicyPublicTests|BlobObjectKeyLifetimePublicTests|BlobPartialReadBoundaryPublicTests|BlobReadPolicyPublicTests|BlobRejectedContentPublicTests|BlobReservationQuotaPublicTests|BlobRowCreatorPublicTests|BlobScopedUploadIdentityPublicTests|BlobSupersessionPublicTests|DeclarativeCompositionReverseQuotaClusterTests|GraphVertexPublicWholeFlowTests|NativeTextDistinctTermsPublicTests|NativeTextNestedWildcardPublicTests|NativeTextNonStringTransitionsPublicTests|NativeTextNormalizationPublicTests|NativeTextProtectedPublicColdTests|NativeTextRawReadPolicyPublicTests|NativeTextRequestRefusalPublicTests|NativeTextRowOwnershipPublicTests|PostgresDecimalClusterWholeFlowTests|PostgresErrorReplyReceiverClusterTests|PostgresNativeSessionClusterTests|PostgresPortalOwnershipClusterTests|PostgresPositiveExecuteClusterTests|ProjectionPublicColdTests|QueueLeaseFencingClusterTests|QueueLifecyclePublicClusterTests|SqlFetchCountClusterWholeFlowTests|SqlNullDistinctClusterWholeFlowTests|SqlNullOrderingClusterWholeFlowTests|SqlOffsetClusterWholeFlowTests|SubscriptionPhasePublicColdTests|TypedEndpointJoinClusterTests)/*");
localImageFilters.add("/*/*/(BlobMaintenancePolicyPublicTests|BlobMaximumRangePublicTests|BlobTombstoneListingPublicTests|SdkProblemBudgetPublicTests|TimeSeriesAggregateScanBoundClusterTests|TimeSeriesCurrentCapabilityClusterTests)/*");
localImageFilters.add("/*/*/(BlobActiveUploadSlotPublicTests|BlobBoundedRetirementPublicTests|BlobCompleteCasPublicTests|BlobEmptySlotPublicTests|BlobListRowPolicyPublicTests|BlobMaintenancePolicyPublicTests|BlobMaximumRangePublicTests|BlobObjectKeyLifetimePublicTests|BlobPartialReadBoundaryPublicTests|BlobReadPolicyPublicTests|BlobRejectedContentPublicTests|BlobReservationQuotaPublicTests|BlobRowCreatorPublicTests|BlobScopedUploadIdentityPublicTests|BlobSupersessionPublicTests|BlobTombstoneListingPublicTests|DeclarativeCompositionReverseQuotaClusterTests|GraphVertexPublicWholeFlowTests|NativeTextDistinctTermsPublicTests|NativeTextNestedWildcardPublicTests|NativeTextNonStringTransitionsPublicTests|NativeTextNormalizationPublicTests|NativeTextProtectedPublicColdTests|NativeTextRawReadPolicyPublicTests|NativeTextRequestRefusalPublicTests|NativeTextRowOwnershipPublicTests|PostgresDecimalClusterWholeFlowTests|PostgresErrorReplyReceiverClusterTests|PostgresInt16ClusterWholeFlowTests|PostgresNativeSessionClusterTests|PostgresPortalOwnershipClusterTests|PostgresPositiveExecuteClusterTests|ProjectionPublicColdTests|QueueLeaseFencingClusterTests|QueueLifecyclePublicClusterTests|SdkProblemBudgetPublicTests|SqlFetchCountClusterWholeFlowTests|SqlNullDistinctClusterWholeFlowTests|SqlNullOrderingClusterWholeFlowTests|SqlOffsetClusterWholeFlowTests|SubscriptionPhasePublicColdTests|TimeSeriesAggregateScanBoundClusterTests|TimeSeriesCurrentCapabilityClusterTests|TypedEndpointJoinClusterTests)/*");
localImageFilters.add("/*/*/EarlyAdmissionQualificationTests/*");
localImageFilters.add("/*/*/QueryRuleOptimizationClusterTests/*");
localImageFilters.add("/*/*/StrongReadSessionAbsenceClusterTests/*");
localImageFilters.add("/*/*/TimeSeriesChunkQualificationClusterTests/*");
localImageFilters.add("/*/*/(EarlyAdmissionQualificationTests|QueryRuleOptimizationClusterTests|StrongReadSessionAbsenceClusterTests|TimeSeriesChunkQualificationClusterTests)/*");
localImageFilters.add("/*/*/SafeProjectorOperationClusterTests/*");
localImageFilters.add("/*/*/SqlParserAdmissionClusterWholeFlowTests/*");
localImageFilters.add("/*/*/GraphSearchPathPolicyClusterTests/*");
localImageFilters.add("/*/*/(SqlParserAdmissionClusterWholeFlowTests|GraphSearchPathPolicyClusterTests)/*");
localImageFilters.add("/*/*/(SqlParserAdmissionClusterWholeFlowTests|GraphSearchPathPolicyClusterTests|SagaDueClusterTests|SagaTimeoutClusterTests|DocumentSessionReadClusterTests|StrongReadSessionAbsenceClusterTests)/*");
localImageFilters.add("/*/*/GraphSearchLineageClusterTests/*");
localImageFilters.add("/*/*/AtomicTriadStagedClusterTests/*");
localImageFilters.add("/*/*/CompetingInboxPublicTests/*");
localImageFilters.add("/*/*/QueueViewLeaseIsolationTests/*");
localImageFilters.add("/*/*/(SafeProjectorOperationClusterTests|SqlParserAdmissionClusterWholeFlowTests|GraphSearchLineageClusterTests|AtomicTriadStagedClusterTests|CompetingInboxPublicTests|QueueViewLeaseIsolationTests)/*");
localImageFilters.add("/*/*/SlowBodyAdmissionReturnTests/*");
localImageFilters.add("/*/*/RowRevisionInferenceClusterTests/*");
localImageFilters.add("/*/*/(SlowBodyAdmissionReturnTests|RowRevisionInferenceClusterTests)/*");
localImageFilters.add("/*/*/CatalogBootstrapAuthorityClusterTests/*");
localImageFilters.add("/*/*/(SlowBodyAdmissionReturnTests|RowRevisionInferenceClusterTests|CatalogBootstrapAuthorityClusterTests)/*");
localImageFilters.add("/*/*/FoundationOtlpClusterTests/NativeDashboardReceivesActualClusterSdkMcpPolicyRepairTracesAndPrivateFreeRejectionLogs");
localImageFilters.add("/*/*/AspireContainerFaultCommandTests/WrongStaleAndForeignIdsRefuseWithoutEffectsBeforeHealthySdkMcpRestart");
localImageFilters.add("/*/*/AggregateReplayClusterLeaderLossTests/AcEvent008AcceptedRestartCancellationResumesSameReplacementAndPreservesReplay");
localImageFilters.add("/*/*/FunctionalDatabaseGateTests/SingleInstanceRunsEveryModelThroughSdkMcpAndSqlWithPersistedUsers");
localImageFilters.add("/*/*/FunctionalDatabaseGateTests/ThreeNodeClusterRunsSameEveryModelScenarioThroughSdkMcpAndSql");
localImageFilters.add("/*/*/PartitionQueryPublicValidationTests/*");
localImageFilters.add("/*/*/CrossTenantClusterWholeFlowTests/ForeignWritesScansAndIndexesAreDeniedAndAuthorizedReceiptRemainsStable");
localImageFilters.add("/*/*/FunctionalDatabaseColdReopenTests/SingleInstanceColdReopensAllModelsAndReplaysOriginalPublicCommands");
localImageFilters.add("/*/*/FunctionalDatabaseColdReopenTests/ThreeNodeColdReopensAllModelsAndReplaysOriginalPublicCommands");
localImageFilters.add("/*/*/FunctionalFileOwnershipClusterTests/DistinctNativeFilesRetainAllModelsAcrossCompetingOwnersAndTwoColdReopens");
const localImageGithubIdentity = ['KEYLOAD_IMAGE_RECEIPT', 'GITHUB_SHA', 'GITHUB_ACTIONS'];
const localImageInheritedIdentity = ['KEYLOAD_IMAGE_PROVENANCE', 'KEYLOAD_LOCAL_IMAGE_RECEIPT',
  'KeyLoad__ContainerImages__Server', 'KEYLOAD_LOCAL_CLUSTER_IMAGE_CHILD',
  'KEYLOAD_TUNIT_NATIVE_COVERAGE_ARGUMENTS', localImageArgumentsEnvironment];
const mapped = new Map([
  ['Filter', '--treenode-filter'], ['ResultsDirectory', '--results-directory'],
  ['CoverageSettings', '--coverage-settings'], ['CoverageOutput', '--coverage-output'],
  ['CoverageFormat', '--coverage-output-format'], ['Execution:MaximumParallelTests', '--maximum-parallel-tests']
]);
const retiredBenchmarkSelector = /^(?:ScaleProfile|VectorProfile|OpenLoopRate|OpenLoopCancellationProof)$/iu;

function rejectRetiredBenchmarkSelection(input, inherited) {
  for (const argument of input) {
    const match = /^--KeyLoadTests:([^=]+)(?:=|$)/iu.exec(argument);
    if (match && retiredBenchmarkSelector.test(match[1])) throw new Error('Retired benchmark selection is not supported.');
    if (/^--Benchmarks:/iu.test(argument)) throw new Error('Benchmark workloads use their native session entry.');
  }
  for (const key of Object.keys(inherited)) {
    const match = /^(?:KeyLoadTests|Benchmarks)(?:__|:)([^:]+)$/iu.exec(key);
    if (match && retiredBenchmarkSelector.test(match[1])) throw new Error('Retired benchmark selection is not supported.');
  }
}

// Selection only: workloads and Aspire resource lifetimes belong to the C# TUnit tests.
export function nativeSelection(input, inherited = process.env) {
  rejectRetiredBenchmarkSelection(input, inherited);
  if (nativeSimdOptions.some(key => inherited[key] !== undefined && inherited[key] !== '' && inherited[key] !== '1')) {
    throw new Error('Native SIMD must remain enabled.');
  }
  const values = new Map();
  for (const argument of input) {
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
    throw new Error('Heavy CLUSTER load requires its explicit enabled selection.');
  }
  if (heavyLoad && (values.get(heavyLoadEnabledOption) !== 'true' || suite !== 'cluster'
    || values.get('Filter') !== heavyLoadFilter || values.has('CoverageSettings')
    || [...values.keys()].some(key => key.startsWith('NativeCoverage:')))) {
    throw new Error('Heavy CLUSTER load requires its exact exclusive functional selection.');
  }
  const parallel = Number(values.get('Execution:MaximumParallelTests')
    ?? (suite === 'comparison' || heavyLoad ? comparisonParallelTests : defaultMaximumParallelTests));
  if (!Number.isInteger(parallel) || parallel < 1 || parallel > maximumParallelTests) throw new Error('Invalid native parallelism.');
  if ((suite === 'comparison' || heavyLoad) && parallel !== comparisonParallelTests) {
    throw new Error('Comparison measurements and heavy load require exactly one native test at a time.');
  }
  const localImageEnabled = values.get('LocalClusterImage:Enabled');
  const localImageSelected = values.has('LocalClusterImage:Enabled');
  if (localImageSelected && (localImageEnabled !== 'true' || suite !== 'cluster'
    || !localImageFilters.has(values.get('Filter'))
    || [...values.keys()].some(key => !['Suite', 'Filter', 'LocalClusterImage:Enabled',
      'ReportTrx', 'TimeoutMinutes', 'ResultsDirectory', 'Execution:MaximumParallelTests'].includes(key))
    || localImageGithubIdentity.some(key => inherited[key] !== undefined && inherited[key] !== '')
    || localImageInheritedIdentity.some(key => inherited[key] !== undefined && inherited[key] !== ''))) {
    throw new Error('Invalid native local CLUSTER image selection.');
  }
  if (suite === 'cluster' && !values.has('Filter')) values.set('Filter', ordinaryClusterFilter);
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
  for (const key of ['KeyLoadTests__Suite', 'KeyLoadTests__LocalClusterImage__Enabled', 'KeyLoadTests__HeavyLoad__Enabled',
    'KEYLOAD_TUNIT_NATIVE_COVERAGE_ARGUMENTS',
    localImageArgumentsEnvironment]) delete environment[key];
  if (localImageSelected) {
    environment[localImageArgumentsEnvironment] = JSON.stringify([
      '--KeyLoadTests:Suite=' + suite, '--KeyLoadTests:Filter=' + values.get('Filter'),
      '--KeyLoadTests:LocalClusterImage:Enabled=true'
    ]);
  }
  const args = ['test', '--project', `tests/${project}`, '--no-build', '--no-restore', '--configuration', 'Release',
    '--output', 'Detailed', '--github-reporter-style', 'full', '--maximum-parallel-tests', String(parallel),
    '--results-directory', path.resolve(root, values.get('ResultsDirectory') ?? `TestResults/${suite}`)];
  args.push('--timeout', `${values.get('TimeoutMinutes') ?? (['cluster', 'comparison'].includes(suite) ? 60 : 30)}m`);
  if (suite === 'comparison') args.push('--minimum-expected-tests', '1');
  const preparation = [];
  for (const [key, value] of values) {
    if (['Suite', 'ResultsDirectory', 'Execution:MaximumParallelTests'].includes(key)) continue;
    if (key === 'ReportTrx') {
      if (!['true', 'false'].includes(value)) throw new Error('Invalid TRX selection.');
      if (value === 'true') args.push('--report-trx');
    } else if (key === 'TimeoutMinutes') {
      const minutes = Number(value);
      if (!Number.isInteger(minutes) || minutes < 1 || minutes > 180) throw new Error('Invalid native timeout.');

    } else if (mapped.has(key)) {
      args.push(mapped.get(key), key.startsWith('Coverage') && key !== 'CoverageFormat' ? path.resolve(root, value) : value);
    } else if (key === heavyLoadEnabledOption) {
      environment.KeyLoadTests__HeavyLoad__Enabled = value;
    } else if (key === 'LocalClusterImage:Enabled') {
      continue;
    } else if (key.startsWith('NativeCoverage:')) {
      if (suite !== 'cluster') throw new Error('Native CLUSTER preparation requires IntegrationTests.');
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
