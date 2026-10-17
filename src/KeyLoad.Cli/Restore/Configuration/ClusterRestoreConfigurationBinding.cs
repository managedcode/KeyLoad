using Microsoft.Extensions.Options;

namespace KeyLoad.Cli.Features.BackupRestore;

/// <summary>Binds one explicit offline restore owner without recapturing per-copy policies.</summary>
[ConfigurationBinding]
internal static class ClusterRestoreConfigurationBinding
{
    private const string RestorePrefix = "KEYLOAD_CLUSTER_RESTORE__";
    private const string LimitsPrefix = "KEYLOAD_DATABASELIMITS__";
    private const int NoConfiguredItems = 0;
    private const string InvalidRestore = "The complete explicit offline cluster restore configuration is required.";
    private const string InvalidLimits = "The original native database limits are invalid.";

    internal static IOptions<ClusterRestoreOperatorConfiguration> Read() =>
        CliStorageConfiguration.Bind<ClusterRestoreOperatorConfiguration>(RestorePrefix,
            static configuration => new ClusterRestoreOptionsFactory(configuration, IsValid, InvalidRestore));

    internal static IOptions<DatabaseLimits> ReadLimits() =>
        CliStorageConfiguration.Bind(LimitsPrefix, static () => new DatabaseLimits(),
            static limits => limits.IsValid(), InvalidLimits);

    private static bool IsValid(ClusterRestoreOperatorConfiguration value) =>
        !string.IsNullOrWhiteSpace(value.DestinationRoot)
        && value.Sources is { Length: > NoConfiguredItems } && value.Sources.All(source => source is not null
            && source.OwnerId != Guid.Empty && !string.IsNullOrWhiteSpace(source.ArchiveDirectory)
            && !string.IsNullOrWhiteSpace(source.ExpectedManifestDigest))
        && value.Mappings is { Length: > NoConfiguredItems } && value.Mappings.All(mapping => mapping is not null
            && mapping.Source is not null && mapping.Target is not null && mapping.Endpoints is not null
            && mapping.Source.VoterIds is not null && mapping.Target.VoterIds is not null)
        && value.Nodes is { Length: > NoConfiguredItems } && value.Nodes.All(node => node is not null)
        && value.Credentials is { Length: > NoConfiguredItems } && value.Credentials.All(credential => credential is not null)
        && value.SigningIdentities is { Length: > NoConfiguredItems } && value.SigningIdentities.All(identity => identity is not null);
}
