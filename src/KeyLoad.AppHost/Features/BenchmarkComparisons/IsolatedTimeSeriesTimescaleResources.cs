using System.Globalization;
using System.Security.Cryptography;

namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal static class IsolatedTimeSeriesTimescaleResources
{
    private const string NodePrefix = "isolated-timescale-";
    private const string PasswordName = "isolated-timescale-password";
    private const string Connection = "ConnectionString";
    private const string Tcp = "tcp";
    private const string Image = "timescale/timescaledb";
    private const string Tag = "2.30.2-pg18";
    private const string Registry = "docker.io/";
    private const string TagSeparator = ":";
    private const string DigestSeparator = "@";
    private const string GuidFormat = "N";
    private const string ContainerSeparator = "-";
    private const string Digest = "sha256:e72689191e1c977892c53d6f2c344dbc4a9657a867dc8cc1899229f9d3672b2e";
    private const string InvalidCount = "IsolatedTimeSeriesTimescaleNodeCountInvalid";
    private const int MinimumNodes = 1;
    private const int MaximumNodes = 3;
    private const int PrimaryOrdinal = 1;
    private const int PrimaryNodeCount = 1;
    private const int FirstEndpointIndex = 0;
    private const int NodeOrdinalOffset = 1;
    private const int DigestPrefixLength = 7;
    private const int SecretBytes = 32;
    private const int Port = 5432;

    internal static void Add(IsolatedTimeSeriesResourceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.NodeCount is < MinimumNodes or > MaximumNodes)
        {
            throw new InvalidOperationException(InvalidCount);
        }

        var scripts = IsolatedPostgresBootstrap.FindScripts(context.Builder);
        var password = context.Builder.AddParameter(PasswordName,
            Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(SecretBytes)), secret: true);
        var privateGroup = Guid.NewGuid().ToString(GuidFormat, CultureInfo.InvariantCulture);
        var primaryName = NodePrefix + PrimaryOrdinal;
        var primary = AddPrimary(context, primaryName, password, privateGroup);
        IsolatedPostgresBootstrap.Configure(primary, context.DataDirectory(primaryName), scripts, password, null,
            context.NodeCount - PrimaryNodeCount, primaryName);
        context.BindSetting(Connection, primary.Resource.ConnectionStringExpression);
        context.BindEndpoint(FirstEndpointIndex, primary, Tcp);
        AddStandbys(context, primary, password, scripts, privateGroup, primaryName);
        context.BindImage(Registry + Image + TagSeparator + Tag + DigestSeparator + Digest);
    }

    private static IResourceBuilder<PostgresServerResource> AddPrimary(IsolatedTimeSeriesResourceContext context,
        string name, IResourceBuilder<ParameterResource> password, string privateGroup)
        => context.Builder.AddPostgres(name, password: password).WithImage(Image).WithImageTag(Tag)
            .WithImageSHA256(Digest[DigestPrefixLength..]).WithContainerName(name + ContainerSeparator + privateGroup)
            .WithContainerNetworkAlias(name);

    private static void AddStandbys(IsolatedTimeSeriesResourceContext context,
        IResourceBuilder<PostgresServerResource> primary, IResourceBuilder<ParameterResource> password,
        (string Entry, string Init) scripts, string privateGroup, string primaryName)
    {
        for (var index = NodeOrdinalOffset; index < context.NodeCount; index++)
        {
            AddStandby(context, primary, password, scripts, privateGroup, primaryName, index);
        }
    }

    private static void AddStandby(IsolatedTimeSeriesResourceContext context,
        IResourceBuilder<PostgresServerResource> primary, IResourceBuilder<ParameterResource> password,
        (string Entry, string Init) scripts, string privateGroup, string primaryName, int index)
    {
        var ordinal = index.ToString(CultureInfo.InvariantCulture);
        var name = NodePrefix + (index + NodeOrdinalOffset).ToString(CultureInfo.InvariantCulture);
        var standby = context.Builder.AddContainer(name, Image, Tag)
            .WithImageSHA256(Digest[DigestPrefixLength..]).WithContainerName(name + ContainerSeparator + privateGroup)
            .WithContainerNetworkAlias(name).WithEndpoint(targetPort: Port, name: Tcp, scheme: Tcp).WaitFor(primary);
        IsolatedPostgresBootstrap.Configure(standby, context.DataDirectory(name), scripts, password, ordinal,
            context.NodeCount - PrimaryNodeCount, primaryName);
        context.BindEndpoint(index, standby, Tcp);
    }
}
