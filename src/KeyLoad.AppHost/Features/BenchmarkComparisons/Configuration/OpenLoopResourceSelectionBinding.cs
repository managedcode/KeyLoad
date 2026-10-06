using System.Globalization;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.Comparisons;
using Microsoft.Extensions.Options;

namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

[ConfigurationBinding]
internal static class OpenLoopResourceSelectionBinding
{
    private const string SelectedProof = "true";
    private const string InvalidSelection = "The native open-loop resource selection is invalid.";
    private const string SettingSeparator = ":";
    private const string EnvironmentSeparator = "__";

    internal static OpenLoopResourceSelection? Read(IConfiguration configuration, ComparisonWorkerSelection selection)
    {
        var proofText = configuration[TestSuiteProtocol.OpenLoopCancellationProofSetting];
        if (selection.OpenLoopRate is not { } rate)
        {
            if (proofText is not null) { throw new InvalidOperationException(InvalidSelection); }
            return null;
        }
        if (proofText is not null && proofText != SelectedProof
            || proofText is not null && (selection.Target != OpenLoopProtocolIdentities.KeyLoadTarget
                || selection.NodeCount != OpenLoopExecutionOptions.DefaultMaximumNodes
                || selection.Scenario != Scenario.PointRead))
        {
            throw new InvalidOperationException(InvalidSelection);
        }
        var factory = new OptionsFactory<OpenLoopExecutionOptions>(
            [new ConfigureFromConfigurationOptions<OpenLoopExecutionOptions>(
                configuration.GetSection(OpenLoopExecutionOptions.SectionName))], [],
            [new OpenLoopExecutionOptionsValidator()]);
        IOptions<OpenLoopExecutionOptions> options = new OptionsManager<OpenLoopExecutionOptions>(factory);
        _ = options.Value;
        return new(rate, proofText is not null);
    }

    internal static void Apply(IResourceBuilder<ContainerResource> runner, ComparisonWorkerSelection selection,
        OpenLoopResourceSelection? openLoop)
    {
        if (selection.ScaledProfile is { } scaleProfile)
        {
            Bind(runner, ComparisonWorkerSelection.ScaleProfileSetting, scaleProfile.Id);
        }
        if (openLoop is not null)
        {
            Bind(runner, ComparisonWorkerSelection.OpenLoopRateSetting,
                openLoop.OfferedRatePerSecond.ToString(CultureInfo.InvariantCulture));
            if (openLoop.CancellationProof)
            {
                Bind(runner, TestSuiteProtocol.OpenLoopCancellationProofSetting, SelectedProof);
            }
        }
    }

    private static void Bind(IResourceBuilder<ContainerResource> runner, string setting, string value)
        => runner.WithEnvironment(setting.Replace(SettingSeparator, EnvironmentSeparator, StringComparison.Ordinal), value);
}
