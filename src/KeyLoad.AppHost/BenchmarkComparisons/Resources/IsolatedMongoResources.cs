using System.Globalization;
using System.Security.Cryptography;

namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal static class IsolatedMongoResources
{
    private const string Target = "MongoDB";
    private const string NodePrefix = "isolated-mongo-";
    private const string PasswordName = "isolated-mongo-password";
    private const string KeyName = "isolated-mongo-replication-key";
    private const string User = "benchmark";
    private const string ReplicaSet = "benchmark";
    private const string Connection = "ConnectionString";
    private const string Tcp = "tcp";
    private const string Image = "mongo";
    private const string Tag = "8.3.9";
    private const string ImageReference = "docker.io/library/" + Image + ":" + Tag;
    private const string MongoScheme = "mongodb://";
    private const string PasswordSeparator = ":";
    private const string CredentialsSeparator = "@";
    private const string DigestSeparator = "@";
    private const string ConnectionOptions = "/admin?authSource=admin";
    private const string RetryOptions = "&retryWrites=false&retryReads=false";
    private const string NodePort = ":27017";
    private const int Port = 27017;
    private const int DigestPrefixLength = 7;
    private const int SecretBytes = 32;
    private const string ReplicaConnection = "&replicaSet=" + ReplicaSet;
    private const string InvalidSelection = "IsolatedMongoSelectionInvalid";
    private const string GroupFormat = "N";
    private const string GroupSeparator = "-";

    internal static void Add(IsolatedResourceContext context)
    {
        const int BoundaryValue = 1;
        const int IndexInitialValue = 0;
        const int Step = 1;
        const char SeparatorCharacter = ',';

        ArgumentNullException.ThrowIfNull(context);
        context.Selection.Validate();
        if (context.Selection.Target != Target)
        {
            throw new InvalidOperationException(InvalidSelection);
        }
        var scripts = IsolatedMongoBootstrap.FindScripts(context);
        var password = Secret(context, PasswordName);
        var key = context.Selection.NodeCount > BoundaryValue ? Secret(context, KeyName) : null;
        var nodes = new List<IResourceBuilder<ContainerResource>>(context.Selection.NodeCount);
        var group = Guid.NewGuid().ToString(GroupFormat);
        for (var index = IndexInitialValue; index < context.Selection.NodeCount; index++)
        {
            var name = NodePrefix + (index + Step).ToString(CultureInfo.InvariantCulture);
            var node = context.Builder.AddContainer(name, Image, Tag).WithImageSHA256(BenchmarkResources.MongoDigest[DigestPrefixLength..])
                .WithContainerName(name + GroupSeparator + group).WithContainerNetworkAlias(name)
                .WithEndpoint(targetPort: Port, name: Tcp, scheme: Tcp);
            IsolatedMongoBootstrap.ConfigureNode(node, context.DataDirectory(name), scripts.Entry, User, password, key);
            nodes.Add(node);
            context.BindEndpoint(index, node, Tcp);
        }
        var hosts = string.Join(SeparatorCharacter, nodes.Select(node => node.Resource.Name + NodePort));
        var replica = key is null ? string.Empty : ReplicaConnection;
        context.BindSetting(Connection, ReferenceExpression.Create(
            $"{MongoScheme}{User}{PasswordSeparator}{password}{CredentialsSeparator}{hosts}{ConnectionOptions}{replica}{RetryOptions}"));
        IsolatedMongoBootstrap.AddClient(context, nodes, scripts.Client, scripts.Readiness, User, password, ReplicaSet, Image, Tag, hosts);
        context.BindImage(ImageReference + DigestSeparator + BenchmarkResources.MongoDigest);
    }

    private static IResourceBuilder<ParameterResource> Secret(IsolatedResourceContext context, string name)
        => context.Builder.AddParameter(name, Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(SecretBytes)), secret: true);
}
