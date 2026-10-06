using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

/// <summary>Binds the existing generated-runner environment names through native options.</summary>
[ConfigurationBinding]
internal static class BenchmarkScenarioSelectionRegistration
{
    private const string EngineEnvironment = "KEYLOAD_RAW_STORAGE_ENGINE";
    private const string RecordCountEnvironment = "KEYLOAD_SCALED_STORAGE_RECORD_COUNT";
    private const string RawEngineFailure = "The raw-storage engine label is unsupported.";
    private const string ScaledEngineFailure = "The scaled benchmark engine label is unsupported.";
    private const string RecordCountFailure = "The scaled benchmark record-count selection is unsupported.";
    private const string SettingSeparator = ":";

    internal static IOptions<BenchmarkScenarioSelectionOptions> ReadRaw() => Read(false);
    internal static IOptions<BenchmarkScenarioSelectionOptions> ReadScaled() => Read(true);

    internal static int RecordCount(BenchmarkScenarioSelectionOptions snapshot)
    {
        if (snapshot.RecordCount is null) { return BenchmarkScenarioSelectionOptions.HundredThousandRecords; }
        if (int.TryParse(snapshot.RecordCount, NumberStyles.None, CultureInfo.InvariantCulture, out var count)
            && count is BenchmarkScenarioSelectionOptions.HundredThousandRecords or BenchmarkScenarioSelectionOptions.OneMillionRecords)
        { return count; }
        throw new InvalidOperationException(RecordCountFailure);
    }

    private static IOptions<BenchmarkScenarioSelectionOptions> Read(bool scaled)
    {
        var values = new Dictionary<string, string?>
        {
            [BenchmarkScenarioSelectionOptions.SectionName + SettingSeparator + nameof(BenchmarkScenarioSelectionOptions.Engine)] =
                Environment.GetEnvironmentVariable(EngineEnvironment),
            [BenchmarkScenarioSelectionOptions.SectionName + SettingSeparator + nameof(BenchmarkScenarioSelectionOptions.RecordCount)] =
                scaled ? Environment.GetEnvironmentVariable(RecordCountEnvironment) : null
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        using var configurationLifetime = configuration as IDisposable;
        return BenchmarkScenarioOptionsRegistration.Read<BenchmarkScenarioSelectionOptions>(configuration,
            BenchmarkScenarioSelectionOptions.SectionName, settings => Validate(settings, scaled), RecordCountFailure);
    }

    private static bool Validate(BenchmarkScenarioSelectionOptions snapshot, bool scaled)
    {
        ValidateEngine(snapshot.Engine, scaled);
        if (scaled) { _ = RecordCount(snapshot); }
        return true;
    }

    private static void ValidateEngine(string label, bool scaled)
    {
        if (label != BenchmarkScenarioSelectionOptions.ZoneTree)
        { throw new ArgumentException(scaled ? ScaledEngineFailure : RawEngineFailure, nameof(label)); }
    }
}
