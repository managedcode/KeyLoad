using System.Globalization;
using System.Security.Cryptography;

namespace KeyLoad.LoadTests.AppHost.BenchmarkComparisons;

/// <summary>Runs the Release-published KeyLoad server image as digest-pinned containers on Aspire's container network.</summary>
internal static class IsolatedKeyLoadResources
{
    private const string Target = "KeyLoad";
    private const string NodePrefix = "isolated-keyload-";
    private const string GroupSeparator = "-";
    private const string GroupFormat = "N";
    private const string GuidFormat = "D";
    private const string ClusterPrefix = "keyload-";
    private const string HttpEndpoint = "http";
    private const string ReadyPath = "/health/ready";
    private const string OriginPrefix = "http://";
    private const string PortSeparator = ":";
    private const string MembershipParameter = "membership-secret";
    private const string SigningParameter = "signing-key";
    private const string AdminParameter = "admin-key";
    private const string IncarnationParameter = "incarnation";
    private const string AdminPrefix = "root.";
    private const string AdminEnvironment = "Benchmarks__AdminKey";
    private const string ClusterEnvironment = "KeyLoad__ClusterId";
    private const string SiloNameEnvironment = "KeyLoad__SiloName";
    private const string SiloCountEnvironment = "KeyLoad__SiloCount";
    private const string ReplicaCountEnvironment = "KeyLoad__ReplicaCount";
    private const string StorageModeEnvironment = "KeyLoad__StorageMode";
    private const string IsolatedStorage = "IsolatedVolumes";
    private const string SiloAddressEnvironment = "KeyLoad__SiloAddress";
    private const string SiloPortEnvironment = "KeyLoad__SiloPort";
    private const string PublicEnvironment = "KeyLoad__PublicEndpoint";
    private const string DataEnvironment = "KeyLoad__DataDirectory";
    private const string IncarnationEnvironment = "KeyLoad__Incarnation";
    private const string SigningEnvironment = "KeyLoad__SigningKey";
    private const string AdminKeyEnvironment = "KeyLoad__AdminKey";
    private const string MembershipSecretEnvironment = "KeyLoad__RapidMembership__SharedSecret";
    private const string MembershipPortEnvironment = "KeyLoad__RapidMembership__ListenPort";
    private const string LogEnvironment = "Logging__LogLevel__Default";
    // The image user's home exists in the base image, so a fresh volume there inherits that user's ownership.
    private const string HomeMount = "/home/app";
    private const string DataDirectory = HomeMount + "/data";
    private const string InvalidTarget = "IsolatedKeyLoadSelectionInvalid";
    private const int HttpPort = 8080;
    private const int SiloPort = 11111;
    private const int MembershipPort = 11112;
    private const int SecretBytes = 32;

    internal static void Add(IsolatedResourceContext context)
    {
        const int FirstNodeIndex = 0;
        const int NodeNumberOffset = 1;
        ArgumentNullException.ThrowIfNull(context);
        context.Selection.Validate();
        if (context.Selection.Target != Target)
        {
            throw new InvalidOperationException(InvalidTarget);
        }
        var image = KeyLoadReleaseImage.Parse(context.Selection.KeyLoadImage);
        var incarnation = Guid.NewGuid();
        var secrets = AddSecrets(context.Builder, incarnation);
        var group = Guid.NewGuid().ToString(GroupFormat);
        var nodeCount = context.Selection.NodeCount.ToString(CultureInfo.InvariantCulture);
        for (var index = FirstNodeIndex; index < context.Selection.NodeCount; index++)
        {
            var name = NodePrefix + (index + NodeNumberOffset).ToString(CultureInfo.InvariantCulture);
            var node = context.Builder.AddContainer(name, KeyLoadReleaseImage.Repository, image.Tag).WithImageSHA256(image.Digest)
                .WithContainerName(name + GroupSeparator + group).WithContainerNetworkAlias(name)
                .WithVolume(HomeMount).WithHttpEndpoint(targetPort: HttpPort, name: HttpEndpoint)
                .WithHttpHealthCheck(ReadyPath, endpointName: HttpEndpoint)
                .WithEnvironment(ClusterEnvironment, ClusterPrefix + incarnation.ToString(GroupFormat))
                .WithEnvironment(SiloNameEnvironment, name).WithEnvironment(SiloCountEnvironment, nodeCount)
                .WithEnvironment(ReplicaCountEnvironment, nodeCount).WithEnvironment(StorageModeEnvironment, IsolatedStorage)
                .WithEnvironment(SiloAddressEnvironment, name)
                .WithEnvironment(SiloPortEnvironment, SiloPort.ToString(CultureInfo.InvariantCulture))
                .WithEnvironment(PublicEnvironment, OriginPrefix + name + PortSeparator + HttpPort.ToString(CultureInfo.InvariantCulture))
                .WithEnvironment(DataEnvironment, DataDirectory)
                .WithEnvironment(IncarnationEnvironment, secrets.Incarnation).WithEnvironment(SigningEnvironment, secrets.Signing)
                .WithEnvironment(AdminKeyEnvironment, secrets.Admin).WithEnvironment(MembershipSecretEnvironment, secrets.Membership)
                .WithEnvironment(MembershipPortEnvironment, MembershipPort.ToString(CultureInfo.InvariantCulture))
                .WithEnvironment(LogEnvironment, context.Deployment.Value.KeyLoadLogLevel);
            context.BindEndpoint(index, node, HttpEndpoint);
        }
        context.Client.Set(AdminEnvironment, secrets.Admin.Resource);
        context.BindImage(image.Reference);
    }

    private static (IResourceBuilder<ParameterResource> Signing, IResourceBuilder<ParameterResource> Admin,
        IResourceBuilder<ParameterResource> Incarnation, IResourceBuilder<ParameterResource> Membership)
        AddSecrets(IDistributedApplicationBuilder builder, Guid incarnationId)
    {
        var signing = builder.AddParameter(SigningParameter,
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(SecretBytes)), secret: true);
        var admin = builder.AddParameter(AdminParameter,
            AdminPrefix + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(SecretBytes)), secret: true);
        var incarnation = builder.AddParameter(IncarnationParameter, incarnationId.ToString(GuidFormat), secret: true);
        var membership = builder.AddParameter(MembershipParameter,
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(SecretBytes)), secret: true);
        return (signing, admin, incarnation, membership);
    }
}
