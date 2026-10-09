using KeyLoad.IntegrationTests.Features.CodeQuality;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

/// <summary>Checks the actual native caller before any owned Aspire resource acquisition.</summary>
[ConfigurationBinding]
internal static class HeavyDocumentLoadAdmission
{
    internal const string FilterArgument = "--treenode-filter";
    internal const string ExactFilter = "/*/*/HeavyDocumentLoadRf3Tests/*";
    internal const string ParallelismArgument = "--maximum-parallel-tests";
    internal const string ExclusiveParallelism = "1";
    internal const string CoveragePreparationEnvironment = "KEYLOAD_TUNIT_NATIVE_COVERAGE_ARGUMENTS";
    internal const string CoverageSection = "KeyLoadTests:NativeCoverage";
    private const string CoverageArgumentPrefix = "--coverage";
    private const string ArgumentEquals = "=";

    internal static void ValidateRuntime()
    {
        using var configuration = new ConfigurationManager();
        configuration.AddEnvironmentVariables();
        Validate(configuration, Environment.GetCommandLineArgs());
    }

    internal static void Validate(ConfigurationManager configuration, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(arguments);
        var options = new OptionsManager<HeavyDocumentLoadExecutionOptions>(new AdmissionFactory(configuration));
        _ = options.Value;
        if (!ExactlyOne(arguments, FilterArgument, ExactFilter)
            || !ExactlyOne(arguments, ParallelismArgument, ExclusiveParallelism)
            || arguments.Any(argument => argument.StartsWith(CoverageArgumentPrefix, StringComparison.Ordinal))
            || configuration[CoveragePreparationEnvironment] is not null
            || configuration[NativeCoverageRf3FixtureProtocol.ModeEnvironment] is not null
            || configuration.GetSection(CoverageSection).GetChildren().Any())
        { throw new InvalidOperationException(HeavyDocumentLoadExecutionOptions.ValidationMessage); }
    }

    private static bool ExactlyOne(IReadOnlyList<string> arguments, string name, string expected)
    {
        var matches = arguments.Select((argument, index) => (argument, index))
            .Where(item => item.argument == name || item.argument.StartsWith(name + ArgumentEquals, StringComparison.Ordinal)).ToArray();
        return matches.Length == 1 && matches[0].argument == name
            && matches[0].index + 1 < arguments.Count && arguments[matches[0].index + 1] == expected;
    }

    [ConfigurationBinding]
    private sealed class AdmissionFactory(ConfigurationManager configuration) : OptionsFactory<HeavyDocumentLoadExecutionOptions>(
        [new ConfigureFromConfigurationOptions<HeavyDocumentLoadExecutionOptions>(
            configuration.GetSection(HeavyDocumentLoadExecutionOptions.SectionName))], [],
        [new ValidateOptions<HeavyDocumentLoadExecutionOptions>(Options.DefaultName,
            value => value.Enabled, HeavyDocumentLoadExecutionOptions.ValidationMessage)])
    {
        protected override HeavyDocumentLoadExecutionOptions CreateInstance(string name) => new();
    }
}
