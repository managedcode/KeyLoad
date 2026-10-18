using System.Globalization;
using KeyLoad.Comparisons;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.ComparisonHost.Features.BenchmarkComparisons;

/// <summary>Retains original GitHub provenance when execution belongs to a workflow; local runs have none.</summary>
/// <param name="Provenance">The actual workflow run and evidence profile, or no publication provenance for local execution.</param>
[KeyLoad.ConfigurationBinding]
internal sealed record ComparisonExecutionIdentity(GitHubProvenance? Provenance)
{
    private const string InvalidIdentityCode = "ComparisonExecutionIdentityInvalid";
    private const string MissingSettingPrefix = "Missing benchmark setting: ";
    private const string SourceRevisionSetting = "Benchmarks:SourceRevision";
    private const string EvidenceProfileSetting = "Benchmarks:EvidenceProfile";
    private const string RunIdSetting = "GITHUB_RUN_ID";
    private const string RunAttemptSetting = "GITHUB_RUN_ATTEMPT";
    private const string RepositorySetting = "GITHUB_REPOSITORY";
    private const string GitHubRefSetting = "GITHUB_REF";
    private const string WorkflowSetting = "GITHUB_WORKFLOW";
    private const string GitHubShaSetting = "GITHUB_SHA";
    private const string GitHubActionsSetting = "GITHUB_ACTIONS";
    private const string CanonicalRepository = "managedcode/KeyLoad";
    private const string CanonicalRef = "refs/heads/main";
    private const string CanonicalWorkflow = "Benchmarks";
    private const int RevisionLength = 40;

    /// <summary>Requires exact workflow identity in Actions and leaves local execution explicitly unqualified for publication.</summary>
    internal static ComparisonExecutionIdentity ReadSession(IConfiguration configuration, string? sourceRevision,
        ComparisonTopology topology)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var profile = Required(configuration, EvidenceProfileSetting);
        if (!Enum.IsDefined(topology) || profile != BenchmarkSessionContract.Profile)
        {
            throw InvalidIdentity();
        }
        if (!string.Equals(configuration[GitHubActionsSetting], bool.TrueString, StringComparison.OrdinalIgnoreCase))
        {
            return new(Provenance: null);
        }
        var runId = PositiveInt64(Required(configuration, RunIdSetting));
        var runAttempt = PositiveInt32(Required(configuration, RunAttemptSetting));
        var repository = Nonempty(Required(configuration, RepositorySetting));
        var gitHubRef = Nonempty(Required(configuration, GitHubRefSetting));
        var workflow = Nonempty(Required(configuration, WorkflowSetting));
        var gitHubSha = Required(configuration, GitHubShaSetting);
        var configuredRevision = Required(configuration, SourceRevisionSetting);
        if (!IsRevision(sourceRevision) || sourceRevision != configuredRevision
            || !IsRevision(gitHubSha) || sourceRevision != gitHubSha
            || repository != CanonicalRepository || gitHubRef != CanonicalRef || workflow != CanonicalWorkflow)
        {
            throw InvalidIdentity();
        }
        return new(new GitHubProvenance(runId, runAttempt, repository, gitHubRef, workflow, profile));
    }

    private static string Required(IConfiguration configuration, string key)
        => configuration[key] ?? throw new InvalidOperationException(MissingSettingPrefix + key);

    private static string Nonempty(string value)
        => string.IsNullOrWhiteSpace(value) ? throw InvalidIdentity() : value;

    private static long PositiveInt64(string value)
        => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > ComparisonExecutionIdentityValues.FirstIndex
            ? parsed
            : throw InvalidIdentity();

    private static int PositiveInt32(string value)
        => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > ComparisonExecutionIdentityValues.FirstIndex
            ? parsed
            : throw InvalidIdentity();

    private static bool IsRevision(string? value)
        => value is { Length: RevisionLength }
            && value.All(static character => character is >= ComparisonExecutionIdentityValues.FirstDecimalDigit and <= ComparisonExecutionIdentityValues.LastDecimalDigit or >= ComparisonExecutionIdentityValues.ACharacter and <= ComparisonExecutionIdentityValues.FCharacter);

    private static InvalidOperationException InvalidIdentity() => new(InvalidIdentityCode);
}
