using Microsoft.Extensions.Configuration;

namespace KeyLoad.AppHost.Features.TestInfrastructure.Validation;

[ConfigurationBinding]
internal sealed record LocalRf3ImageRequest
{
    private const int HasProtocolCohortSelectorCountValue = 1;

    private const string EnabledValue = "true";
    private const string DisabledValue = "false";
    private const string LocalProvenance = "local-development";
    private const string Rf3Suite = "rf3";
    private const string EnabledKey = "Enabled";

    internal const string EnabledSetting = "KeyLoadTests:LocalRf3Image:Enabled";
    internal const string EnabledEnvironment = "KeyLoadTests__LocalRf3Image__Enabled";
    private const string ProvenanceSetting = "KEYLOAD_IMAGE_PROVENANCE";
    private const string GithubReceiptSetting = "KEYLOAD_IMAGE_RECEIPT";
    private const string LocalReceiptSetting = "KEYLOAD_LOCAL_IMAGE_RECEIPT";
    private const string ServerImageSetting = "KeyLoad:ContainerImages:Server";
    private const string ChildSetting = "KEYLOAD_LOCAL_RF3_IMAGE_CHILD";
    private const string LocalSectionSetting = "KeyLoadTests:LocalRf3Image";
    private const string GithubRevisionSetting = "GITHUB_SHA";
    private const string GithubActionsSetting = "GITHUB_ACTIONS";
    private const string ProtocolCohortEnabledSetting = "KeyLoadTests:ProtocolCohort:Enabled";
    private const string ProtocolNode1Setting = "KeyLoadTests:ProtocolCohort:Voters:node1";
    private const string ProtocolNode2Setting = "KeyLoadTests:ProtocolCohort:Voters:node2";
    private const string ProtocolNode3Setting = "KeyLoadTests:ProtocolCohort:Voters:node3";
    private const string ProtocolSectionSetting = "KeyLoadTests:ProtocolCohort";
    private const string ComparisonEnabledSetting = "Benchmarks:Enabled";
    private const string ComparisonTargetSetting = "Benchmarks:Target";
    private const string ComparisonNodeCountSetting = "Benchmarks:NodeCount";
    private const string ComparisonScenarioSetting = "Benchmarks:Scenario";
    private const string ComparisonProfileSetting = "Benchmarks:Profile";
    private const string ComparisonScaleProfileSetting = "Benchmarks:ScaleProfile";
    private const string Invalid = "Local RF3 image mode configuration is invalid.";

    internal static bool ReadEnabled(IConfiguration configuration, string? suite, string? filter)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var enabledText = ReadEnabledText(configuration);

        if (enabledText is not null && enabledText is not (EnabledValue or DisabledValue))
        {
            throw new InvalidOperationException(Invalid);
        }
        var enabled = enabledText == EnabledValue;
        ValidateLocalSection(configuration);
        var child = configuration[ChildSetting];
        if (!string.IsNullOrEmpty(child))
        {
            return ValidateChildRequest(configuration, child, enabledText, suite);
        }

        if (!enabled)
        {
            ValidateDisabledRequest(configuration);
            return false;
        }

        ValidateEnabledRequest(configuration, suite, filter);
        return true;
    }

    private static bool ValidateChildRequest(IConfiguration configuration, string child, string? enabledText,
        string? suite)
    {
        if (child != EnabledValue || enabledText is not null || !string.IsNullOrWhiteSpace(suite)
            || !string.Equals(configuration[ProvenanceSetting], LocalProvenance, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(configuration[LocalReceiptSetting])
            || string.IsNullOrWhiteSpace(configuration[ServerImageSetting])
            || HasValue(configuration, GithubReceiptSetting) || HasValue(configuration, GithubRevisionSetting)
            || HasValue(configuration, GithubActionsSetting)
            || HasProtocolCohortSelector(configuration) || HasComparisonSelector(configuration))
        {
            throw new InvalidOperationException(Invalid);
        }
        return false;
    }

    private static void ValidateDisabledRequest(IConfiguration configuration)
    {
        if (HasValue(configuration, LocalReceiptSetting) || HasValue(configuration, ProvenanceSetting))
        {
            throw new InvalidOperationException(Invalid);
        }
    }

    private static void ValidateEnabledRequest(IConfiguration configuration, string? suite, string? filter)
    {
        if (suite != Rf3Suite || string.IsNullOrWhiteSpace(filter)
            || HasValue(configuration, ProvenanceSetting)
            || HasValue(configuration, LocalReceiptSetting)
            || HasValue(configuration, GithubReceiptSetting)
            || HasValue(configuration, ServerImageSetting)
            || HasValue(configuration, GithubRevisionSetting)
            || HasValue(configuration, GithubActionsSetting)
            || HasProtocolCohortSelector(configuration)
            || HasComparisonSelector(configuration))
        {
            throw new InvalidOperationException(Invalid);
        }
    }

    private static string? ReadEnabledText(IConfiguration configuration)
    {
        var text = configuration[EnabledSetting];
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static void ValidateLocalSection(IConfiguration configuration)
    {
        const int CountValue = 2;
        const int BoundaryValue = 1;

        var localSection = configuration.GetSection(LocalSectionSetting);
        var localChildren = localSection.GetChildren().Take(CountValue).ToArray();
        if (localSection.Value is not null || localChildren.Length > BoundaryValue
            || localChildren.Any(child => child.Key != EnabledKey || child.GetChildren().Take(HasProtocolCohortSelectorCountValue).Any())
            || configuration.GetSection(EnabledSetting).GetChildren().Any())
        {
            throw new InvalidOperationException(Invalid);
        }
    }

    private static bool HasProtocolCohortSelector(IConfiguration configuration)
        => HasValue(configuration, ProtocolCohortEnabledSetting)
            || HasValue(configuration, ProtocolNode1Setting)
            || HasValue(configuration, ProtocolNode2Setting)
            || HasValue(configuration, ProtocolNode3Setting)
            || configuration.GetSection(ProtocolSectionSetting).GetChildren().Take(HasProtocolCohortSelectorCountValue).Any();

    private static bool HasComparisonSelector(IConfiguration configuration)
        => HasValue(configuration, ComparisonEnabledSetting)
            || HasValue(configuration, ComparisonTargetSetting)
            || HasValue(configuration, ComparisonNodeCountSetting)
            || HasValue(configuration, ComparisonScenarioSetting)
            || HasValue(configuration, ComparisonProfileSetting)
            || HasValue(configuration, ComparisonScaleProfileSetting);

    private static bool HasValue(IConfiguration configuration, string key)
        => !string.IsNullOrWhiteSpace(configuration[key]);
}
