using System.Collections.Immutable;
using System.Globalization;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

/// <summary>Native executable resource and secret parameters; it never starts an independent process.</summary>
internal static class ClusterRestoreRf3Operator
{
    private const string Prefix = "KEYLOAD_CLUSTER_RESTORE__";
    private const string Separator = "__";
    private const string SecretPrefix = "restore-secret-";
    private const string SigningName = "signing";
    private const string CredentialName = "credential";
    private const string Runtime = "dotnet";
    private const int FirstIndex = 0;
    private const string ContainerData = "/data";
    private const string SourcesSection = "Sources";
    private const string MappingsSection = "Mappings";
    private const string CredentialsSection = "Credentials";
    private const string SigningSection = "SigningIdentities";
    private const string NodesSection = "Nodes";
    private const string DestinationField = "DestinationRoot";
    private const string OperationField = "OperationId";
    private const string OwnerField = "OwnerId";
    private const string ArchiveField = "ArchiveDirectory";
    private const string ManifestField = "ExpectedManifestDigest";
    private const string SourceOwnerField = "SourceOwnerId";
    private const string CredentialField = "Credential";
    private const string SigningField = "SigningKey";
    private const string SourceField = "Source";
    private const string TargetField = "Target";
    private const string PhysicalField = "PhysicalShardId";
    private const string IncarnationField = "Incarnation";
    private const string EpochField = "PlacementEpoch";
    private const string VotersField = "VoterIds";
    private const string EndpointsField = "Endpoints";
    private const string VoterField = "VoterId";
    private const string DirectoryField = "RelativeDataDirectory";

    internal static IResourceBuilder<ExecutableResource> Add(IDistributedApplicationBuilder builder,
        string repository, string publicationRoot, ImmutableArray<ClusterBackupOwnerReceipt> receipts,
        ImmutableArray<string> archives, ImmutableArray<ClusterRestoreOwnerMapping> mappings,
        string credential, string signingKey, Guid operationId, NativeClusterRestoreStage? cut = null)
    {
        var cli = System.IO.Path.Combine(repository, cut is null ? ClusterRestoreRf3Protocol.CliDll
            : ClusterRestoreRf3ResumeProtocol.CrashHostDll);
        if (!File.Exists(cli) || receipts.Length != archives.Length || receipts.Length != mappings.Length)
        { throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid); }
        var resource = builder.AddExecutable(ClusterRestoreRf3Protocol.RestoreResource, Runtime, repository,
            cut is { } selected ? [cli, ClusterRestoreRf3ResumeProtocol.CutMode, selected.ToString(),
                operationId.ToString(ClusterRestoreRf3Protocol.IdentityFormat)] : [cli, ClusterRestoreRf3Protocol.CliCommand]);
        resource.WithEnvironment(Prefix + DestinationField, publicationRoot);
        resource.WithEnvironment(Prefix + OperationField, operationId.ToString(ClusterRestoreRf3Protocol.IdentityFormat));
        var credentialParameter = builder.AddParameter(SecretPrefix + CredentialName, credential, secret: true);
        var signingParameter = builder.AddParameter(SecretPrefix + SigningName, signingKey, secret: true);
        var nodeIndex = FirstIndex;
        for (var index = FirstIndex; index < receipts.Length; index++)
        {
            var receipt = receipts[index];
            var mapping = mappings.Single(value => value.Source.PhysicalShardId == receipt.Cut.Owner.PhysicalShardId);
            var number = Index(index);
            Set(resource, SourcesSection, number, OwnerField, receipt.Cut.Owner.PhysicalShardId.ToString(ClusterRestoreRf3Protocol.IdentityFormat));
            Set(resource, SourcesSection, number, ArchiveField, archives[index]);
            Set(resource, SourcesSection, number, ManifestField, receipt.ManifestDigest);
            Set(resource, CredentialsSection, number, SourceOwnerField, mapping.Source.PhysicalShardId.ToString(ClusterRestoreRf3Protocol.IdentityFormat));
            resource.WithEnvironment(Path(CredentialsSection, number, CredentialField), credentialParameter);
            Set(resource, SigningSection, number, SourceOwnerField, mapping.Source.PhysicalShardId.ToString(ClusterRestoreRf3Protocol.IdentityFormat));
            resource.WithEnvironment(Path(SigningSection, number, SigningField), signingParameter);
            Owner(resource, number, SourceField, mapping.Source);
            Owner(resource, number, TargetField, mapping.Target);
            for (var voter = FirstIndex; voter < mapping.Target.VoterIds.Length; voter++)
            {
                Set(resource, MappingsSection, number, EndpointsField + Separator + Index(voter), mapping.Endpoints[voter]);
                Node(resource, Index(nodeIndex), mapping, voter, ClusterRestoreRf3Protocol.Nodes[nodeIndex]);
                nodeIndex++;
            }
        }
        if (nodeIndex != ClusterRestoreRf3Protocol.Nodes.Length)
        { throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid); }
        return resource;
    }

    internal static void BindTargetData(IDistributedApplicationBuilder builder, string profileRoot,
        string publicationRoot, IResourceBuilder<ExecutableResource>? cli)
    {
        var nodes = builder.Resources.OfType<ContainerResource>().Where(resource =>
            ClusterRestoreRf3Protocol.Nodes.Contains(resource.Name, StringComparer.Ordinal)).ToArray();
        if (nodes.Length != ClusterRestoreRf3Protocol.Nodes.Length)
        { throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid); }
        foreach (var node in nodes)
        {
            var mount = node.Annotations.OfType<ContainerMountAnnotation>().Single(value => value.Target == ContainerData);
            if (mount.Type != ContainerMountType.BindMount || mount.IsReadOnly
                || !string.Equals(mount.Source, System.IO.Path.Combine(profileRoot, node.Name), StringComparison.Ordinal))
            { throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid); }
            node.Annotations.Remove(mount);
            node.Annotations.Add(new ContainerMountAnnotation(System.IO.Path.Combine(publicationRoot, node.Name), ContainerData,
                ContainerMountType.BindMount, isReadOnly: false));
            if (cli is not null)
            { builder.CreateResourceBuilder(node).WaitForCompletion(cli, ClusterRestoreRf3Protocol.SuccessfulExit); }
        }
    }

    private static void Owner(IResourceBuilder<ExecutableResource> resource, string index, string field,
        PhysicalShardRecord owner)
    {
        var prefix = field + Separator;
        Set(resource, MappingsSection, index, prefix + PhysicalField, owner.PhysicalShardId.ToString(ClusterRestoreRf3Protocol.IdentityFormat));
        Set(resource, MappingsSection, index, prefix + IncarnationField, owner.Incarnation.ToString(ClusterRestoreRf3Protocol.IdentityFormat));
        Set(resource, MappingsSection, index, prefix + EpochField, owner.PlacementEpoch.ToString(CultureInfo.InvariantCulture));
        for (var voter = FirstIndex; voter < owner.VoterIds.Length; voter++)
        { Set(resource, MappingsSection, index, prefix + VotersField + Separator + Index(voter), owner.VoterIds[voter]); }
    }

    private static void Node(IResourceBuilder<ExecutableResource> resource, string index,
        ClusterRestoreOwnerMapping mapping, int voter, string node)
    {
        Set(resource, NodesSection, index, SourceOwnerField, mapping.Source.PhysicalShardId.ToString(ClusterRestoreRf3Protocol.IdentityFormat));
        Set(resource, NodesSection, index, VoterField, mapping.Target.VoterIds[voter]);
        Set(resource, NodesSection, index, DirectoryField, node);
    }

    private static string Index(int value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Path(string section, string index, string field) => Prefix + section + Separator + index + Separator + field;
    private static void Set(IResourceBuilder<ExecutableResource> resource, string section, string index,
        string field, string value) => resource.WithEnvironment(Path(section, index, field), value);
}
