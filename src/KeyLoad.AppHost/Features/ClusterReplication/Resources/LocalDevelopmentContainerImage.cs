using System.Text.RegularExpressions;
using KeyLoad.AppHost.Features.TestInfrastructure.Execution;

namespace KeyLoad.AppHost.Features.ClusterReplication;

/// <summary>Reads the explicit local-development image identity without weakening GitHub image parsing.</summary>
internal sealed partial record LocalDevelopmentContainerImage(string Reference, string Tag, string ReceiptPath)
{
    private const string GithubReceiptSetting = "KEYLOAD_IMAGE_RECEIPT";
    private const string GithubRevisionSetting = "GITHUB_SHA";
    private const string GithubActionsSetting = "GITHUB_ACTIONS";
    private const string ServerImageSetting = "KeyLoad:ContainerImages:Server";
    private const string ChildSetting = "KEYLOAD_LOCAL_RF3_IMAGE_CHILD";
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
    private const string Invalid = "Local RF3 container image configuration is invalid.";
    private const string TagPrefix = "local-";
    internal const string Repository = "keyload/local-server";
    private const int MaximumReceiptPathCharacters = 256;

    internal static LocalDevelopmentContainerImage? Read(IDistributedApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var provenance = builder.Configuration[LocalRf3ImageExecution.ProvenanceEnvironment];
        var reference = builder.Configuration[ServerImageSetting];
        var receiptPath = builder.Configuration[LocalRf3ImageExecution.ReceiptEnvironment];
        var child = builder.Configuration[ChildSetting];
        if (provenance is null && receiptPath is null && child is null)
        {
            return null;
        }

        if (HasInvalidLocalImageRequest(builder, provenance, reference, receiptPath, child))
        {
            throw new InvalidOperationException(Invalid);
        }

        var acceptedReference = reference!;
        var tag = acceptedReference[(acceptedReference.IndexOf(':', StringComparison.Ordinal) + 1)..];
        var invocation = tag[TagPrefix.Length..];
        var expectedReceipt = $"TestResults/rf3/local-images/image-{invocation}.json";
        if (!string.Equals(receiptPath, expectedReceipt, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(Invalid);
        }
        return new(acceptedReference, tag, receiptPath!);
    }

    private static bool HasInvalidLocalImageRequest(IDistributedApplicationBuilder builder, string? provenance,
        string? reference, string? receiptPath, string? child)
        => HasInvalidImageIdentity(provenance, reference, receiptPath, child)
            || HasConflictingImageSelectors(builder);

    private static bool HasInvalidImageIdentity(string? provenance, string? reference, string? receiptPath, string? child)
        => provenance != LocalRf3ImageExecution.Provenance || reference is null || receiptPath is null
            || child != "true"
            || reference!.Length > 256 || !ImageReference().IsMatch(reference)
            || receiptPath!.Length is 0 or > MaximumReceiptPathCharacters
            || Path.IsPathRooted(receiptPath) || receiptPath.Contains('\\', StringComparison.Ordinal);

    private static bool HasConflictingImageSelectors(IDistributedApplicationBuilder builder)
        => HasValue(builder, GithubReceiptSetting)
            || HasValue(builder, GithubRevisionSetting)
            || HasValue(builder, GithubActionsSetting)
            || HasValue(builder, ProtocolCohortEnabledSetting)
            || HasValue(builder, ProtocolNode1Setting)
            || HasValue(builder, ProtocolNode2Setting)
            || HasValue(builder, ProtocolNode3Setting)
            || builder.Configuration.GetSection(ProtocolSectionSetting).GetChildren().Take(1).Any()
            || HasComparisonSelector(builder);

    private static bool HasComparisonSelector(IDistributedApplicationBuilder builder)
        => HasValue(builder, ComparisonEnabledSetting)
            || HasValue(builder, ComparisonTargetSetting)
            || HasValue(builder, ComparisonNodeCountSetting)
            || HasValue(builder, ComparisonScenarioSetting)
            || HasValue(builder, ComparisonProfileSetting)
            || HasValue(builder, ComparisonScaleProfileSetting);

    private static bool HasValue(IDistributedApplicationBuilder builder, string key)
        => !string.IsNullOrWhiteSpace(builder.Configuration[key]);

    [GeneratedRegex("\\Akeyload/local-server:local-[a-f0-9]{32}\\z", RegexOptions.CultureInvariant, 100)]
    private static partial Regex ImageReference();
}
