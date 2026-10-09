using System.Text.Json;

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
    private const string ChildEnvironment = "KEYLOAD_LOCAL_RF3_IMAGE_CHILD";
    private const string NativeCoverageArgumentsEnvironment = "KEYLOAD_TUNIT_NATIVE_COVERAGE_ARGUMENTS";
    private const string NativeArgumentsEnvironment = "KEYLOAD_TUNIT_LOCAL_RF3_IMAGE_ARGUMENTS";
    private const string Rf3SuiteArgument = "--KeyLoadTests:Suite=rf3";
    private const string RejectionFilterArgument = "--KeyLoadTests:Filter=/*/*/RelationalSqlRf3JoinRejectionTests/*";
    private const string MembershipFilterArgument = "--KeyLoadTests:Filter=/*/*/TwoRf3MembershipProfileTests/*";
    private const string ConnectionFilterArgument = "--KeyLoadTests:Filter=/*/*/(ConnectionRf3SequentialTests|ConnectionRf3OverlapTests|ConnectionRf3AuthorizationTests)/*";
    private const string StandardFilterArgument = "--KeyLoadTests:Filter=/*/*/(PartitionQueryMcpSchemaTests|RelationalSqlRf3JoinTests|RelationalSqlRf3JoinAuthorizationTests|RelationalSqlRf3JoinBudgetTests|RelationalSqlRf3JoinCancellationTests|RelationalSqlRf3JoinReadCutTests)/*";
    private const string EnabledArgument = "--KeyLoadTests:LocalRf3Image:Enabled=true";
    private const string EnabledValue = "true";
    private const int MaximumNativeArgumentCharacters = 4096;
    internal const string Provenance = "local-development";
    internal const string Repository = "keyload/local-server";
    private const int MaximumReceiptPathCharacters = 256;

    internal sealed record Selection(string Reference, string Tag, string Receipt, string ChildMarker)
    {
        internal string[] CreateWaveArguments() =>
        [
            "--KEYLOAD_IMAGE_PROVENANCE=" + Provenance,
            "--KeyLoad:ContainerImages:Server=" + Reference,
            "--KEYLOAD_LOCAL_IMAGE_RECEIPT=" + Receipt,
            "--KEYLOAD_LOCAL_RF3_IMAGE_CHILD=" + ChildMarker
        ];
    }

    internal static Selection? Read()
    {
        var provenance = Environment.GetEnvironmentVariable(ProvenanceEnvironment);
        var reference = Environment.GetEnvironmentVariable(ReferenceEnvironment);
        var receipt = Environment.GetEnvironmentVariable(ReceiptEnvironment);
        if (provenance is null && receipt is null)
        {
            return null;
        }
        return CreateSelection(provenance, reference, receipt, Environment.GetEnvironmentVariable(ChildEnvironment),
            requireChild: false, rejectAmbientGithub: true);
    }

    internal static string[]? ReadNativeArgumentsIfSelected()
    {
        var value = Environment.GetEnvironmentVariable(NativeArgumentsEnvironment);
        if (value is null)
        {
            return null;
        }
        if (value.Length is 0 or > MaximumNativeArgumentCharacters)
        {
            throw new InvalidOperationException("The local RF3 image selection is invalid.");
        }
        var arguments = JsonSerializer.Deserialize<string[]>(value);
        if (arguments is not [Rf3SuiteArgument, MembershipFilterArgument or StandardFilterArgument or RejectionFilterArgument or ConnectionFilterArgument, EnabledArgument]
            || Environment.GetEnvironmentVariable(NativeCoverageArgumentsEnvironment) is not null
            || HasAmbientGithubIdentity()
            || Environment.GetEnvironmentVariable(ProvenanceEnvironment) is not null
            || Environment.GetEnvironmentVariable(ReceiptEnvironment) is not null
            || Environment.GetEnvironmentVariable(ReferenceEnvironment) is not null
            || Environment.GetEnvironmentVariable(ChildEnvironment) is not null)
        {
            throw new InvalidOperationException("The local RF3 image selection is invalid.");
        }
        return arguments;
    }

    internal static Selection FromRunnerConfiguration(IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        values.TryGetValue(ProvenanceEnvironment, out var provenance);
        values.TryGetValue(ReferenceEnvironment, out var reference);
        values.TryGetValue(ReceiptEnvironment, out var receipt);
        values.TryGetValue(ChildEnvironment, out var child);
        if (values.TryGetValue(GitHubReceiptEnvironment, out var githubReceipt) && !string.IsNullOrWhiteSpace(githubReceipt)
            || values.TryGetValue(GitHubRevisionEnvironment, out var githubRevision) && !string.IsNullOrWhiteSpace(githubRevision)
            || values.TryGetValue(GitHubActionsEnvironment, out var githubActions) && !string.IsNullOrWhiteSpace(githubActions))
        {
            throw new InvalidOperationException("The local RF3 image identity is invalid.");
        }
        return CreateSelection(provenance, reference, receipt, child, requireChild: true, rejectAmbientGithub: false);
    }

    private static Selection CreateSelection(string? provenance, string? reference, string? receipt, string? child,
        bool requireChild, bool rejectAmbientGithub)
    {
        if (provenance != Provenance || reference is null || receipt is null
            || requireChild && child != EnabledValue
            || rejectAmbientGithub && HasAmbientGithubIdentity()
            || receipt.Length is 0 or > MaximumReceiptPathCharacters || Path.IsPathRooted(receipt)
            || receipt.Contains('\\', StringComparison.Ordinal) || !reference.StartsWith(Repository + ":local-", StringComparison.Ordinal)
            || !IsCanonicalReferenceAndReceipt(reference, receipt))
        {
            throw new InvalidOperationException("The local RF3 image identity is invalid.");
        }
        var tag = reference[(reference.IndexOf(':', StringComparison.Ordinal) + 1)..];
        return new(reference, tag, receipt, child ?? EnabledValue);
    }

    private static bool HasAmbientGithubIdentity()
        => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(GitHubReceiptEnvironment))
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(GitHubRevisionEnvironment))
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(GitHubActionsEnvironment));

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
