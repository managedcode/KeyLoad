using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace KeyLoad.Analyzers.Features.CodeQuality;

internal static class ImplementationTermCatalog
{
    internal const string DiagnosticId = "KLD0038";
    internal const string Title = "Implementation terminology requires explicit approval";
    internal const string Message = "Term '{0}' requires explicit owner approval in this implementation context";
    internal const string Description = "Only the explicitly approved native runtime, domain and testing meanings are permitted. Renaming an unauthorized mechanism does not authorize it.";
    internal const string NamespacePrefix = "KeyLoad.";
    internal const string RuleVocabularyType = "KeyLoad.Analyzers.Features.CodeQuality.ImplementationTermCatalog";
    internal const string TimeSeriesPath = "/TimeSeries/";
    internal const string TestPath = "/tests/";
    internal const string BenchmarkPath = "/benchmarks/";
    internal const string AspireHostPath = "/build/KeyLoad.AppHost/";
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
    internal const string SampleFieldsType = "KeyLoad.Core.Features.InternalSerialization.SampleReadScopeFields";
    internal const string DataAssembly = "KeyLoad.Abstractions";
    internal const string DataCoreAssembly = "KeyLoad.Core";
    internal const string DataRuntimeAssembly = "KeyLoad.Clustering";
    internal const string DataRuntimeNamespace = "KeyLoad.Clustering";
    internal const string DataServerAssembly = "KeyLoad.Server";
    internal const string DataRecordFamiliesType = "KeyLoad.Core.Features.ClusterRouting.Contracts.PartitionRecordFamilies";
    internal const string DataReadKindType = "KeyLoad.Clustering.GrainReadKind";
    internal const string DataRequestPhaseType = "KeyLoad.Clustering.GrainRequestPhase";
    internal const string DataToolDescriptionsType = "KeyLoad.Server.McpToolDescriptions";
    internal const string DataNamespace = "KeyLoad";
    internal const string DataFeatureNamespaceSuffix = ".Features.TimeSeries";
    internal static readonly string[] NativeNamespaces =
    [
        "System", "Microsoft", "Orleans", "ManagedCode", "Tenray.ZoneTree", "ZoneTree", "TUnit", "Aspire", "BenchmarkDotNet"
    ];
    internal static readonly string[] DataNamespaces =
    [
        "KeyLoad", "KeyLoad.Core.Features.TimeSeries", "KeyLoad.Clustering.Features.TimeSeries"
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
        "SampleChunkAdmissionPersisted", "SampleChunkNativeJobReturned", "SampleChunkAdmissionRefused"
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
        "KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons.SampleChunkBenchmarkCorpus",
        "KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons.SampleChunkBenchmarkManifest",
        "KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons.SampleChunkBenchmarkState",
        "KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons.SampleChunkSerializationBenchmarks"
    ];
    internal static readonly string[] DataControlMembers =
    [
        "KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons.BenchmarkArtifactOptions.SampleChunkDirectory",
        "KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons.BenchmarkArtifactOptions.InvalidSource"
    ];
    internal static readonly string[] NativeModelContracts =
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
        "KeyLoad.SiteTests.Features.BenchmarkComparisons.SiteIsolatedFixture",
        "KeyLoad.AppHost.Features.BenchmarkComparisons.ScaleServerContainer",
        "KeyLoad.AppHost.Features.BenchmarkComparisons.ScaleServerObservationPolicy",
        "KeyLoad.AppHost.Features.BenchmarkComparisons.ScaleServerResourceSampleBudget",
        "KeyLoad.AppHost.Features.BenchmarkComparisons.ScaleServerResourceSampler",
        "KeyLoad.AppHost.Features.BenchmarkComparisons.ScaleServerResourceEvidenceCollector"
    ];
    internal static readonly string[] MeasurementMembers =
    [
        "KeyLoad.Comparisons.NativeComparisonExecutionOptions.ResourceSampleIntervalMilliseconds",
        "KeyLoad.Comparisons.NativeComparisonOperationalPolicy.DefaultResourceSampleIntervalMilliseconds",
        "KeyLoad.Comparisons.NativeComparisonOperationalPolicy.MaximumResourceSampleIntervalMilliseconds",
        "KeyLoad.Comparisons.VectorComparisonProfile.LatencySampleCount",
        "KeyLoad.Comparisons.VectorComparisonProfileValues.LatencySampleCount",
        "KeyLoad.Comparisons.VectorProfileJsonFields.LatencySampleCount",
        "KeyLoad.AppHost.Features.BenchmarkComparisons.ScaleServerResourceOptions.SupportedMaxSampleMetadataBytes",
        "KeyLoad.AppHost.Features.BenchmarkComparisons.ScaleServerResourceOptions.SupportedMaxSamples",
        "KeyLoad.AppHost.Features.BenchmarkComparisons.ScaleServerResourceOptions.MaxSampleMetadataBytes",
        "KeyLoad.AppHost.Features.BenchmarkComparisons.ScaleServerResourceOptions.MaxSamples",
        "KeyLoad.AppHost.Features.BenchmarkComparisons.ScaleServerResourceBounds.MinimumSamples",
        "KeyLoad.AppHost.Features.BenchmarkComparisons.ScaleServerResourceBounds.SamplingMissing"
    ];

    private static readonly Regex Words = new("[A-Z]?[a-z]+|[A-Z]+(?![a-z])", RegexOptions.CultureInvariant);

    internal static IEnumerable<(string Term, string Text)> Read(string text)
    {
        var previous = string.Empty;
        foreach (Match word in Words.Matches(text))
        {
            var value = word.Value.ToUpperInvariant();
            var term = Classify(value, previous);
            term ??= word.Value == value ? Compound(value) : null;
            if (term is not null)
            {
                yield return (term, word.Value);
            }
            previous = value;
        }
    }

    private static string? Classify(string value, string previous) => value switch
    {
        "FALLBACK" or "FALLBACKS" => AlternateWord,
        "BACK" when previous == "FALL" => AlternateWord,
        "MIGRATE" or "MIGRATES" or "MIGRATED" or "MIGRATING" or "MIGRATION" or "MIGRATIONS" => MovementWord,
        "REGRESS" or "REGRESSES" or "REGRESSED" or "REGRESSING" or "REGRESSION" or "REGRESSIONS" => PriorFailureWord,
        "MOCK" or "MOCKS" or "MOCKED" or "MOCKING" => SubstituteWord,
        "FIXTURE" or "FIXTURES" => ResourceWord,
        "SAMPLE" or "SAMPLES" or "SAMPLED" or "SAMPLING" => DataWord,
        "SIMPLE" => DemonstrationWord,
        "COMPATIBILITY" or "COMPATIBILITIES" => OlderContractWord,
        "COMPATIBLE" when previous is "BACKWARD" or "BACKWARDS" => OlderContractWord,
        _ => null
    };

    private static string? Compound(string value) => value switch
    {
        _ when value.Contains("FALLBACK", StringComparison.Ordinal) => AlternateWord,
        _ when value.Contains("MIGRAT", StringComparison.Ordinal) => MovementWord,
        _ when value.Contains("REGRESS", StringComparison.Ordinal) => PriorFailureWord,
        _ when value.Contains("MOCK", StringComparison.Ordinal) => SubstituteWord,
        _ when value.Contains("FIXTURE", StringComparison.Ordinal) => ResourceWord,
        _ when value.Contains("SAMPLE", StringComparison.Ordinal) || value.Contains("SAMPLING", StringComparison.Ordinal) => DataWord,
        _ when value.Contains("SIMPLE", StringComparison.Ordinal) => DemonstrationWord,
        _ when value.Contains("COMPATIBILITY", StringComparison.Ordinal) || value.Contains("BACKWARDCOMPATIBLE", StringComparison.Ordinal) ||
            value.Contains("BACKWARDSCOMPATIBLE", StringComparison.Ordinal) => OlderContractWord,
        _ => null
    };

    internal static bool NativeNamespace(string value) => Array.Exists(NativeNamespaces,
        prefix => value == prefix || value.StartsWith(prefix + ".", StringComparison.Ordinal));
}
