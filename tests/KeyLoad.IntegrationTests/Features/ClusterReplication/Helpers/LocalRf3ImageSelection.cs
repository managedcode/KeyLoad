namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Admits the explicit local image selector without treating a shared image reference as local proof.</summary>
internal static class LocalRf3ImageSelection
{
    private const string ProvenanceEnvironment = "KEYLOAD_IMAGE_PROVENANCE";
    private const string ReceiptEnvironment = "KEYLOAD_LOCAL_IMAGE_RECEIPT";
    private const string ReferenceEnvironment = "KeyLoad__ContainerImages__Server";
    private const string GitHubReceiptEnvironment = "KEYLOAD_IMAGE_RECEIPT";
    private const string GitHubRevisionEnvironment = "GITHUB_SHA";
    private const string GitHubActionsEnvironment = "GITHUB_ACTIONS";
    internal const string Provenance = "local-development";
    internal const string Repository = "keyload/local-server";
    private const int MaximumReceiptPathCharacters = 256;

    internal sealed record Selection(string Reference, string Tag, string Receipt);

    internal static Selection? Read()
    {
        var provenance = Environment.GetEnvironmentVariable(ProvenanceEnvironment);
        var reference = Environment.GetEnvironmentVariable(ReferenceEnvironment);
        var receipt = Environment.GetEnvironmentVariable(ReceiptEnvironment);
        if (provenance is null && receipt is null)
        {
            return null;
        }
        ValidateEnvironment(provenance, reference, receipt);
        var validatedReference = reference!;
        var tag = validatedReference[(validatedReference.IndexOf(':', StringComparison.Ordinal) + 1)..];
        return new(validatedReference, tag, receipt!);
    }

    private static void ValidateEnvironment(string? provenance, string? reference, string? receipt)
    {
        if (provenance != Provenance || reference is null || receipt is null
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(GitHubReceiptEnvironment))
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(GitHubRevisionEnvironment))
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(GitHubActionsEnvironment))
            || receipt.Length is 0 or > MaximumReceiptPathCharacters || Path.IsPathRooted(receipt)
            || receipt.Contains('\\', StringComparison.Ordinal) || !reference.StartsWith(Repository + ":local-", StringComparison.Ordinal)
            || !IsCanonicalReferenceAndReceipt(reference, receipt))
        {
            throw new InvalidOperationException("The local RF3 image identity is invalid.");
        }
    }

    private static bool IsCanonicalReferenceAndReceipt(string reference, string receipt)
    {
        var separator = reference.IndexOf(':', StringComparison.Ordinal);
        if (separator < 0 || separator == reference.Length - 1)
        {
            return false;
        }
        var tag = reference[(separator + 1)..];
        if (tag.Length != 38 || !tag.StartsWith("local-", StringComparison.Ordinal)
            || tag[6..].Length != 32 || tag[6..].Any(character => !char.IsAsciiHexDigit(character) || char.IsUpper(character)))
        {
            return false;
        }
        return receipt == $"TestResults/rf3/local-images/image-{tag[6..]}.json";
    }
}
