using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.DocumentStorage;

/// <summary>Checks the actual execution caller before any owned Aspire resource acquisition.</summary>
[ConfigurationBinding]
internal static class HeavyDocumentLoadAdmission
{
    internal const string FilterArgument = "--treenode-filter";
    internal const string ExactFilter = "/*/*/HeavyDocumentLoadClusterTests/*";
    internal const string ParallelismArgument = "--maximum-parallel-tests";
    internal const string ExclusiveParallelism = "1";
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
            || arguments.Any(argument => argument.StartsWith(CoverageArgumentPrefix, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(HeavyDocumentLoadExecutionOptions.ValidationMessage);
        }
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
