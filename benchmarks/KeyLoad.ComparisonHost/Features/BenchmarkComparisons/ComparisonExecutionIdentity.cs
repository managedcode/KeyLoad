using System.Globalization;
using KeyLoad.Comparisons;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.ComparisonHost.Features.BenchmarkComparisons;

/// <summary>Validated GitHub and immutable image identity for a container-backed normal comparison.</summary>
/// <param name="Provenance">The actual workflow run and evidence profile.</param>
/// <param name="KeyLoadImage">The configured KeyLoad server image reference.</param>
/// <param name="LoadGeneratorImage">The configured comparison runner image reference.</param>
internal sealed record ComparisonExecutionIdentity(
    GitHubProvenance Provenance,
    string KeyLoadImage,
    string LoadGeneratorImage)
{
    private const string InvalidIdentityCode = "ComparisonExecutionIdentityInvalid";
    private const string MissingSettingPrefix = "Missing benchmark setting: ";
    private const string LoadGeneratorImageSetting = "Benchmarks:LoadGeneratorImage";
    private const string KeyLoadImageSetting = "Benchmarks:Images:KeyLoad";
    private const string SourceRevisionSetting = "Benchmarks:SourceRevision";
    private const string EvidenceProfileSetting = "Benchmarks:EvidenceProfile";
    private const string RunIdSetting = "GITHUB_RUN_ID";
    private const string RunAttemptSetting = "GITHUB_RUN_ATTEMPT";
    private const string RepositorySetting = "GITHUB_REPOSITORY";
    private const string GitHubRefSetting = "GITHUB_REF";
    private const string WorkflowSetting = "GITHUB_WORKFLOW";
    private const string GitHubShaSetting = "GITHUB_SHA";
    private const string TimeSeriesEvidenceProfile = "timeseries";
    private const string SingleSuffix = "-single";
    private const string ReplicatedSuffix = "-replicated";
    private const int RevisionLength = 40;

    private static readonly string[] EvidenceProfiles =
    [
        "smoke-single",
        "json-1k-c8-single",
        "json-16k-c4-single",
        "smoke-replicated",
        "json-1k-c8-replicated",
        "json-16k-c4-replicated"
    ];

    /// <summary>Reads and validates container provenance after ordinary host settings have been validated.</summary>
    /// <param name="configuration">The comparison host configuration.</param>
    /// <param name="sourceRevision">The revision read through the established host setting.</param>
    /// <param name="topology">The validated comparison topology.</param>
    /// <returns>Null for direct non-container execution, otherwise the complete validated identity.</returns>
    internal static ComparisonExecutionIdentity? Read(IConfiguration configuration, string? sourceRevision,
        ComparisonTopology topology)
        => ReadIdentity(configuration, sourceRevision, topology, isTimeSeries: false);

    /// <summary>Reads the shared image/source identity with the isolated TimeSeries profile contract.</summary>
    /// <param name="configuration">The comparison host configuration.</param>
    /// <param name="sourceRevision">The revision read through the existing TimeSeries setting.</param>
    /// <returns>Null for non-container execution, otherwise the validated TimeSeries identity.</returns>
    internal static ComparisonExecutionIdentity? ReadTimeSeries(IConfiguration configuration, string sourceRevision)
        => ReadIdentity(configuration, sourceRevision, topology: null, isTimeSeries: true);

    private static ComparisonExecutionIdentity? ReadIdentity(IConfiguration configuration, string? sourceRevision,
        ComparisonTopology? topology, bool isTimeSeries)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var loadGeneratorImage = configuration[LoadGeneratorImageSetting];
        if (loadGeneratorImage is null)
        {
            return null;
        }

        var keyLoadImage = Required(configuration, KeyLoadImageSetting);
        var runId = PositiveInt64(Required(configuration, RunIdSetting));
        var runAttempt = PositiveInt32(Required(configuration, RunAttemptSetting));
        var repository = Nonempty(Required(configuration, RepositorySetting));
        var gitHubRef = Nonempty(Required(configuration, GitHubRefSetting));
        var workflow = Nonempty(Required(configuration, WorkflowSetting));
        var gitHubSha = Required(configuration, GitHubShaSetting);
        var configuredRevision = Required(configuration, SourceRevisionSetting);
        var profile = Required(configuration, EvidenceProfileSetting);

        if (!ComparisonExecutionIdentityImageReference.IsValid(keyLoadImage)
            || !ComparisonExecutionIdentityImageReference.IsValid(loadGeneratorImage)
            || !IsRevision(sourceRevision)
            || !string.Equals(sourceRevision, configuredRevision, StringComparison.Ordinal)
            || !IsRevision(gitHubSha)
            || !string.Equals(sourceRevision, gitHubSha, StringComparison.Ordinal)
            || !IsAcceptedProfile(profile, topology, isTimeSeries))
        {
            throw InvalidIdentity();
        }

        return new(new GitHubProvenance(runId, runAttempt, repository, gitHubRef, workflow, profile),
            keyLoadImage, loadGeneratorImage);
    }

    private static string Required(IConfiguration configuration, string key)
        => configuration[key] ?? throw new InvalidOperationException(MissingSettingPrefix + key);

    private static string Nonempty(string value)
        => string.IsNullOrWhiteSpace(value) ? throw InvalidIdentity() : value;

    private static long PositiveInt64(string value)
        => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : throw InvalidIdentity();

    private static int PositiveInt32(string value)
        => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : throw InvalidIdentity();

    private static bool IsRevision(string? value)
        => value is { Length: RevisionLength }
            && value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool IsAcceptedProfile(string profile, ComparisonTopology? topology, bool isTimeSeries)
        => isTimeSeries
            ? string.Equals(profile, TimeSeriesEvidenceProfile, StringComparison.Ordinal)
            : EvidenceProfiles.Contains(profile, StringComparer.Ordinal)
                && (topology == ComparisonTopology.Standalone
                ? profile.EndsWith(SingleSuffix, StringComparison.Ordinal)
                : topology == ComparisonTopology.Replicated
                    && profile.EndsWith(ReplicatedSuffix, StringComparison.Ordinal));

    private static InvalidOperationException InvalidIdentity() => new(InvalidIdentityCode);
}
