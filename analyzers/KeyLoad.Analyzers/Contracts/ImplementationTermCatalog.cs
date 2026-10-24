using System;

namespace KeyLoad.Analyzers.CodeQuality;

internal static class ImplementationTermCatalog
{
    internal const string DiagnosticId = "KLD0038";
    internal const string Title = "Implementation terminology requires explicit approval";
    internal const string Message = "Term '{0}' requires explicit owner approval in this implementation context";
    internal const string Description = "Only the explicitly approved execution runtime, domain and testing meanings are permitted. Renaming an unauthorized mechanism does not authorize it.";
    internal const string MeasurementSamplesMember = "Samples";
    internal const string NamespacePrefix = "KeyLoad.";
    internal const string RuleVocabularyType = "KeyLoad.Analyzers.CodeQuality.ImplementationTermCatalog";
    internal const string RuleReaderType = "KeyLoad.Analyzers.CodeQuality.ImplementationTermReader";
    internal const string TimeSeriesPath = "/TimeSeries/";
    internal const string TestPath = "/tests/";
    internal const string BenchmarkPath = "/benchmarks/";
    internal const string AspireHostPath = "/build/KeyLoad.AppHost/";
    internal const string LoadTestsHostPath = "/build/KeyLoad.LoadTests.AppHost/";
    internal const string TestOwnerPath = "/Fixtures/";
    internal const string OrleansWord = "Orleans";
    internal const string ActivationWord = "activation";
    internal const string DataWord = "sample";
    internal const string ResourceWord = "fixture";
    internal const string MovementWord = "migration";
    internal const string AlternateWord = "fallback";
    internal const string PriorFailureWord = "regression";
    internal const string SubstituteWord = "mock";
    internal const string DemonstrationWord = "simple";
    internal const string OlderContractWord = "compatibility";
    internal const string EarlierWord = "backward";
    internal const string MatchingWord = "compatible";
    internal const string SampleFieldsType = "KeyLoad.Core.InternalSerialization.SampleReadScopeFields";
    internal const string DataAssembly = "KeyLoad.Abstractions";
    internal const string DataCoreAssembly = "KeyLoad.Core";
    internal const string DataRuntimeAssembly = "KeyLoad.Clustering";
    internal const string DataRuntimeNamespace = "KeyLoad.Clustering";
    internal const string DataServerAssembly = "KeyLoad";
    internal const string LoggingProcessorAssembly = "OpenTelemetry";
    internal const string LoggingProcessorType = "OpenTelemetry.SimpleLogRecordExportProcessor";
    internal const string ActivityProcessorType = "OpenTelemetry.SimpleActivityExportProcessor";
    internal const string DataRecordFamiliesType = "KeyLoad.Core.StoragePartitions.Contracts.PartitionRecordFamilies";
    internal const string DataReadKindType = "KeyLoad.Clustering.GrainReadKind";
    internal const string DataRequestPhaseType = "KeyLoad.Clustering.GrainRequestPhase";
    internal const string DataToolDescriptionsType = "KeyLoad.Server.McpToolDescriptions";
    internal const string DataNamespace = "KeyLoad";
    internal const string DataFeatureNamespaceSuffix = ".TimeSeries";
    internal static readonly string[] RuntimeNamespaces =
    [
        "System", "Microsoft", "Orleans", "ManagedCode", "Tenray.ZoneTree", "ZoneTree", "TUnit", "Aspire", "BenchmarkDotNet"
    ];
    internal static readonly string[] DataNamespaces =
    [
        "KeyLoad", "KeyLoad.Core.TimeSeries", "KeyLoad.Clustering.TimeSeries"
    ];
    internal static readonly string[] DataContractTypes =
    [
        "SampleData", "SampleRecord", "AppendSamples", "ExpireSamples", "AggregateSampleWindowsRequest",
        "SampleAggregateWindow", "ReadSamplesRequest", "SampleRollupResult", "ReadSampleRollupRequest",
        "LatestSampleResult", "SampleRollupProtocol", "SampleChunkProtocol", "AggregateSamplesRequest",
        "SampleAggregate", "TimeSeriesReadDefaults", "OpenSampleChunkWindow", "TimeSeriesReadProtocol",
        "DropSampleRollup", "SealSampleChunkWindow", "SampleRollupBucket", "RefreshSampleRollup",
        "SampleRetentionStatus", "ReadSampleRetentionRequest", "MergeSampleChunkWindow",
        "ReadSampleChunkWindowRequest", "DropSampleChunkWindow", "SampleRetentionDefaults",
        "ReadLatestSampleRequest", "SampleAggregateWindowsResult", "SampleChunkWindowResult"
    ];
    internal static readonly string[] DataRecordFamilyFields =
    [
        "Sample", "SampleChunkBlock", "SampleChunkCorrection", "SampleChunkManifest", "SampleChunkWindow",
        "SampleIdentity", "SampleRetention", "SampleRollup", "SampleSequence"
    ];
    internal static readonly string[] DataReadKinds =
    [
        "Samples", "LatestSample", "AggregateSamples", "AggregateSampleWindows", "SampleRetention",
        "SampleRollup", "SampleChunkWindow"
    ];
    internal static readonly string[] DataRequestPhases =
    [
        "SampleChunkAdmissionPersisted", "SampleChunkJobReturned", "SampleChunkAdmissionRefused"
    ];
    internal static readonly string[] DataToolDescriptionFields =
    [
        "SeriesLatest", "SeriesAggregate", "SeriesWindows", "SeriesSampleChunkWindow"
    ];
    internal static readonly string[] DataRuntimeTypes =
    [
        "ISampleChunkWorkerGrain", "SampleChunkWorkerGrain", "ISampleChunkCoordinatorGrain", "SampleChunkCoordinatorGrain"
    ];
    internal static readonly string[] DataControlTypes =
    [
        "KeyLoad.BenchmarkScenarios.BenchmarkComparisons.SampleChunkBenchmarkCorpus",
        "KeyLoad.BenchmarkScenarios.BenchmarkComparisons.SampleChunkBenchmarkManifest",
        "KeyLoad.BenchmarkScenarios.BenchmarkComparisons.SampleChunkBenchmarkState",
        "KeyLoad.BenchmarkScenarios.BenchmarkComparisons.SampleChunkSerializationBenchmarks"
    ];
    internal static readonly string[] DataControlMembers =
    [
        "KeyLoad.BenchmarkScenarios.BenchmarkComparisons.BenchmarkArtifactOptions.SampleChunkDirectory",
        "KeyLoad.BenchmarkScenarios.BenchmarkComparisons.BenchmarkArtifactOptions.InvalidSource",
        "KeyLoad.IntegrationTests.DatabaseComposition.FunctionalGateAnalyticsAssertions.MaximumSamples",
        "KeyLoad.IntegrationTests.DatabaseComposition.FunctionalGateColdMutations.AppendSamplesKind",
        "KeyLoad.IntegrationTests.RelationalStorage.RelationalSqlClusterTokens.SeriesSet",
        "KeyLoad.IntegrationTests.RelationalStorage.RelationalSqlClusterTokens.SampleId",
        "KeyLoad.IntegrationTests.RelationalStorage.RelationalSqlClusterTokens.SampleValue",
        "KeyLoad.IntegrationTests.RelationalStorage.RelationalSqlClusterTokens.SampleAt",
        "KeyLoad.UnitTests.ClientApi.McpMutationTestData.RefreshRollupKind",
        "KeyLoad.UnitTests.ClientApi.McpMutationTestData.DropRollupKind",
        "KeyLoad.UnitTests.ClientApi.McpMutationTestData.Sample",
        "KeyLoad.UnitTests.ClientApi.McpMutationTestData.SamplesKind",
        "KeyLoad.UnitTests.ClientApi.McpMutationTestData.ExpireSamplesKind",
        "KeyLoad.UnitTests.ClientApi.McpMutationTestData.OpenChunkKind",
        "KeyLoad.UnitTests.ClientApi.McpMutationTestData.SealChunkKind",
        "KeyLoad.UnitTests.ClientApi.McpMutationTestData.MergeChunkKind",
        "KeyLoad.UnitTests.ClientApi.McpMutationTestData.DropChunkKind",
        "KeyLoad.UnitTests.OrleansExecution.PartitionRecordInventoryTests.ExpectedFamilies",
        "KeyLoad.UnitTests.BackupRestore.SeriesPortableBackupSeed.EventPrefix",
        "KeyLoad.UnitTests.BackupRestore.SeriesPortableBackupAssertions.MaximumSampleRows",
        "KeyLoad.UnitTests.Search.SearchTests.SampleId",
        "KeyLoad.UnitTests.Search.SearchTests.LargeFiniteSampleValue"
    ];
    internal static readonly string[] ModelContracts =
    [
        "Orleans.Serialization.Codecs.IFieldCodec`1", "Orleans.Serialization.Cloning.IDeepCopier`1",
        "Orleans.Serialization.Serializers.IBaseCodec`1", "Orleans.Serialization.Activators.IActivator`1"
    ];
    internal static readonly string[] MeasurementTypes =
    [
        "KeyLoad.Comparisons.OperationSample", "KeyLoad.Comparisons.BenchmarkSessionOperationSample",
        "KeyLoad.Comparisons.Measurement", "KeyLoad.Comparisons.ClientResources",
        "KeyLoad.Comparisons.ComparisonCase", "KeyLoad.Comparisons.BenchmarkSessionLaneReport",
        "KeyLoad.Comparisons.ClientResourceSampler",
        "KeyLoad.AdminNodeSnapshot", "KeyLoad.OperationalAnalyticsCounters",
        "KeyLoad.OperationalAnalyticsReport", "KeyLoad.OperationalAnalyticsRow",
        "KeyLoad.SiteTests.BenchmarkComparisons.SiteIsolatedFixture",
        "KeyLoad.LoadTests.AppHost.BenchmarkComparisons.ScaleServerContainer",
        "KeyLoad.LoadTests.AppHost.BenchmarkComparisons.ScaleServerObservationPolicy",
        "KeyLoad.LoadTests.AppHost.BenchmarkComparisons.ScaleServerResourceSampleBudget",
        "KeyLoad.LoadTests.AppHost.BenchmarkComparisons.ScaleServerResourceSampler",
        "KeyLoad.LoadTests.AppHost.BenchmarkComparisons.ScaleServerResourceEvidenceCollector",
    ];
    internal static readonly string[] MeasurementMembers =
    [
        "KeyLoad.Comparisons.ComparisonExecutionOptions.ResourceSampleIntervalMilliseconds",
        "KeyLoad.Comparisons.ComparisonOperationalPolicy.DefaultResourceSampleIntervalMilliseconds",
        "KeyLoad.Comparisons.ComparisonOperationalPolicy.MaximumResourceSampleIntervalMilliseconds",
        "KeyLoad.Comparisons.VectorComparisonProfile.LatencySampleCount",
        "KeyLoad.Comparisons.VectorComparisonProfileValues.LatencySampleCount",
        "KeyLoad.Comparisons.VectorProfileJsonFields.LatencySampleCount",
        "KeyLoad.LoadTests.AppHost.BenchmarkComparisons.ScaleServerResourceOptions.SupportedMaxSampleMetadataBytes",
        "KeyLoad.LoadTests.AppHost.BenchmarkComparisons.ScaleServerResourceOptions.SupportedMaxSamples",
        "KeyLoad.LoadTests.AppHost.BenchmarkComparisons.ScaleServerResourceOptions.MaxSampleMetadataBytes",
        "KeyLoad.LoadTests.AppHost.BenchmarkComparisons.ScaleServerResourceOptions.MaxSamples",
        "KeyLoad.LoadTests.AppHost.BenchmarkComparisons.ScaleServerResourceBounds.MinimumSamples",
        "KeyLoad.LoadTests.AppHost.BenchmarkComparisons.ScaleServerResourceBounds.SamplingMissing",
        "KeyLoad.LoadTests.AppHost.BenchmarkComparisons.ScaleServerResourceProcess.ReadBoundedAsync(System.IO.Stream, int, int, System.Threading.CancellationToken)",
        "KeyLoad.LoadTests.AppHost.BenchmarkComparisons.ScaleServerResourceProcess.DrainBoundedAsync(System.IO.Stream, int, System.Threading.CancellationToken)",
    ];

    internal static bool RuntimeNamespace(string value) => Array.Exists(RuntimeNamespaces,
        prefix => value == prefix || value.StartsWith(prefix + ".", StringComparison.Ordinal));
}
