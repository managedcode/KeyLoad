using System.Text.RegularExpressions;
using KeyLoad.AppHost.Hosting;
using KeyLoad.AppHost.Features.TestInfrastructure.Execution;

namespace KeyLoad.AppHost.Features.ClusterReplication;

/// <summary>Reads the explicit local-development image identity without weakening GitHub image parsing.</summary>
internal sealed partial record LocalDevelopmentContainerImage(string Reference, string Tag, string ReceiptPath)
{
    private const string LocalImageReceiptPrefix = "TestResults/rf3/local-images/image-";
    private const string JsonFileSuffix = ".json";

    private const int HasInvalidImageIdentityStructuralValue = 0;
    private const char HasInvalidImageIdentityBackslashCharacter = '\\';
    private const string LocalDevelopmentContainerImageMetadataName = "\\Akeyload/local-server:local-[a-f0-9]{32}\\z";

    private const string EnabledValue = "true";

    private const string Invalid = "Local RF3 container image configuration is invalid.";
    private const string TagPrefix = "local-";
    internal const string Repository = "keyload/local-server";

    internal static LocalDevelopmentContainerImage? Read(IDistributedApplicationBuilder builder)
    {
        const char ColonCharacter = ':';
        const int SecondIndex = 1;

        ArgumentNullException.ThrowIfNull(builder);
        var runtime = AppHostOptionsRegistration.Get(builder);
        var provenance = runtime.LocalImage.Value.Provenance;
        var reference = runtime.Images.Value.Server;
        var receiptPath = runtime.LocalImage.Value.ReceiptPath;
        var child = runtime.LocalImage.Value.Child;
        if (provenance is null && receiptPath is null && child is null)
        {
            return null;
        }

        if (HasInvalidLocalImageRequest(builder, provenance, reference, receiptPath, child))
        {
            throw new InvalidOperationException(Invalid);
        }

        var acceptedReference = reference!;
        var tag = acceptedReference[(acceptedReference.IndexOf(ColonCharacter, StringComparison.Ordinal) + SecondIndex)..];
        var invocation = tag[TagPrefix.Length..];
        var expectedReceipt = $"{LocalImageReceiptPrefix}{invocation}{JsonFileSuffix}";
        if (!string.Equals(receiptPath, expectedReceipt, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(Invalid);
        }
        return new(acceptedReference, tag, receiptPath!);
    }

    private static bool HasInvalidLocalImageRequest(IDistributedApplicationBuilder builder, string? provenance,
        string? reference, string? receiptPath, string? child)
        => HasInvalidImageIdentity(provenance, reference, receiptPath, child, AppHostOptionsRegistration.Get(builder).ImageExecution)
            || HasConflictingImageSelectors(builder);

    private static bool HasInvalidImageIdentity(string? provenance, string? reference, string? receiptPath, string? child, Microsoft.Extensions.Options.IOptions<ContainerImageExecutionOptions> options)
        => provenance != LocalRf3ImageExecution.Provenance || reference is null || receiptPath is null
            || child != EnabledValue
            || reference!.Length > options.Value.MaximumLocalImageCharacters
            || !new Regex(LocalDevelopmentContainerImageMetadataName, RegexOptions.CultureInvariant, options.Value.MatchTimeout).IsMatch(reference)
            || receiptPath!.Length == HasInvalidImageIdentityStructuralValue || receiptPath.Length > options.Value.MaximumReceiptPathCharacters
            || Path.IsPathRooted(receiptPath) || receiptPath.Contains(HasInvalidImageIdentityBackslashCharacter, StringComparison.Ordinal);

    private static bool HasConflictingImageSelectors(IDistributedApplicationBuilder builder)
    {
        var runtime = AppHostOptionsRegistration.Get(builder);
        var local = runtime.LocalImage.Value;
        return !string.IsNullOrWhiteSpace(local.GithubReceipt) || !string.IsNullOrWhiteSpace(local.GithubRevision)
            || !string.IsNullOrWhiteSpace(local.GithubActions) || runtime.Control.Value.ProtocolCohortConfigured
            || runtime.Control.Value.ComparisonSelectorsPresent;
    }
}
