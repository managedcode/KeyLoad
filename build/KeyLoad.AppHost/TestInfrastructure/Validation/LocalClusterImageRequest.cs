using Microsoft.Extensions.Configuration;

namespace KeyLoad.AppHost.Features.TestInfrastructure.Validation;

[ConfigurationBinding]
internal sealed record LocalClusterImageRequest
{
    private const int SectionPresenceCount = 1;

    private const string EnabledValue = "true";
    private const string DisabledValue = "false";
    private const string LocalProvenance = "local-development";
    private const string ClusterSuite = "cluster";
    private const string EnabledKey = "Enabled";

    internal const string EnabledSetting = "KeyLoadTests:LocalClusterImage:Enabled";
    internal const string EnabledEnvironment = "KeyLoadTests__LocalClusterImage__Enabled";
    private const string ProvenanceSetting = "KEYLOAD_IMAGE_PROVENANCE";
    private const string GithubReceiptSetting = "KEYLOAD_IMAGE_RECEIPT";
    private const string LocalReceiptSetting = "KEYLOAD_LOCAL_IMAGE_RECEIPT";
    private const string ServerImageSetting = "KeyLoad:ContainerImages:Server";
    private const string ChildSetting = "KEYLOAD_LOCAL_CLUSTER_IMAGE_CHILD";
    private const string LocalSectionSetting = "KeyLoadTests:LocalClusterImage";
    private const string GithubRevisionSetting = "GITHUB_SHA";
    private const string GithubActionsSetting = "GITHUB_ACTIONS";
    private const string ComparisonEnabledSetting = "Benchmarks:Enabled";
    private const string ComparisonTargetSetting = "Benchmarks:Target";
    private const string ComparisonNodeCountSetting = "Benchmarks:NodeCount";
    private const string ComparisonScenarioSetting = "Benchmarks:Scenario";
    private const string ComparisonProfileSetting = "Benchmarks:Profile";
    private const string ComparisonScaleProfileSetting = "Benchmarks:ScaleProfile";
    private const string Invalid = "Local CLUSTER image mode configuration is invalid.";

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
            || HasComparisonSelector(configuration))
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
        if (suite != ClusterSuite || string.IsNullOrWhiteSpace(filter)
            || HasValue(configuration, ProvenanceSetting)
            || HasValue(configuration, LocalReceiptSetting)
            || HasValue(configuration, GithubReceiptSetting)
            || HasValue(configuration, ServerImageSetting)
            || HasValue(configuration, GithubRevisionSetting)
            || HasValue(configuration, GithubActionsSetting)
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
            || localChildren.Any(child => child.Key != EnabledKey || child.GetChildren().Take(SectionPresenceCount).Any())
            || configuration.GetSection(EnabledSetting).GetChildren().Any())
        {
            throw new InvalidOperationException(Invalid);
        }
    }

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
