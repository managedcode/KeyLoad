using System;

namespace KeyLoad.Analyzers.CodeQuality;

internal static class ImplementationTermCatalog
{
    internal const string DiagnosticId = "KLD0038";
    internal const string Title = "Implementation terminology requires explicit approval";
    internal const string Message = "Term '{0}' requires explicit owner approval in this implementation context";
    internal const string Description = "Only the explicitly approved execution runtime, domain and testing meanings are permitted. Renaming an unauthorized mechanism does not authorize it.";
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
        "KeyLoad.ClusterTests.DatabaseComposition.FunctionalGateAnalyticsAssertions.MaximumSamples",
        "KeyLoad.ClusterTests.DatabaseComposition.FunctionalGateColdMutations.AppendSamplesKind",
        "KeyLoad.ClusterTests.RelationalStorage.RelationalSqlClusterTokens.SeriesSet",
        "KeyLoad.ClusterTests.RelationalStorage.RelationalSqlClusterTokens.SampleId",
        "KeyLoad.ClusterTests.RelationalStorage.RelationalSqlClusterTokens.SampleValue",
        "KeyLoad.ClusterTests.RelationalStorage.RelationalSqlClusterTokens.SampleAt",
        "KeyLoad.FunctionalTests.ClientApi.McpMutationTestData.RefreshRollupKind",
        "KeyLoad.FunctionalTests.ClientApi.McpMutationTestData.DropRollupKind",
        "KeyLoad.FunctionalTests.ClientApi.McpMutationTestData.Sample",
        "KeyLoad.FunctionalTests.ClientApi.McpMutationTestData.SamplesKind",
        "KeyLoad.FunctionalTests.ClientApi.McpMutationTestData.ExpireSamplesKind",
        "KeyLoad.FunctionalTests.ClientApi.McpMutationTestData.OpenChunkKind",
        "KeyLoad.FunctionalTests.ClientApi.McpMutationTestData.SealChunkKind",
        "KeyLoad.FunctionalTests.ClientApi.McpMutationTestData.MergeChunkKind",
        "KeyLoad.FunctionalTests.ClientApi.McpMutationTestData.DropChunkKind",
        "KeyLoad.FunctionalTests.OrleansExecution.PartitionRecordInventoryTests.ExpectedFamilies",
        "KeyLoad.FunctionalTests.BackupRestore.SeriesPortableBackupSeed.EventPrefix",
        "KeyLoad.FunctionalTests.BackupRestore.SeriesPortableBackupAssertions.MaximumSampleRows",
        "KeyLoad.FunctionalTests.Search.SearchTests.SampleId",
        "KeyLoad.FunctionalTests.Search.SearchTests.LargeFiniteSampleValue"
    ];
    internal static readonly string[] ModelContracts =
    [
        "Orleans.Serialization.Codecs.IFieldCodec`1", "Orleans.Serialization.Cloning.IDeepCopier`1",
        "Orleans.Serialization.Serializers.IBaseCodec`1", "Orleans.Serialization.Activators.IActivator`1"
    ];
    internal static readonly string[] MeasurementTypes =
    [
        "KeyLoad.AdminNodeSnapshot", "KeyLoad.OperationalAnalyticsCounters",
        "KeyLoad.OperationalAnalyticsReport", "KeyLoad.OperationalAnalyticsRow",
    ];

    internal static bool RuntimeNamespace(string value) => Array.Exists(RuntimeNamespaces,
        prefix => value == prefix || value.StartsWith(prefix + ".", StringComparison.Ordinal));
}
