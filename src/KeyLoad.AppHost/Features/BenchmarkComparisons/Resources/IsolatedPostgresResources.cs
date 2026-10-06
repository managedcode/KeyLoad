using System.Globalization;
using System.Security.Cryptography;

namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal static class IsolatedPostgresResources
{
    private const string Target = "PostgreSQL + pgvector";
    private const string NodePrefix = "isolated-postgres-";
    private const string PasswordName = "isolated-postgres-password";
    private const string Connection = "ConnectionString";
    private const string Tcp = "tcp";
    private const string Image = "pgvector/pgvector";
    private const string Tag = "0.8.6-pg18";
    private const string Registry = "docker.io/";
    private const string TagSeparator = ":";
    private const string DigestSeparator = "@";
    private const int DigestPrefixLength = 7;
    private const int SecretBytes = 32;
    private const int Port = 5432;
    private const string InvalidSelection = "IsolatedPostgresSelectionInvalid";

    internal static void Add(IsolatedResourceContext context)
    {
        const int Step = 1;
        const int IndexValue = 0;
        const int IndexInitialValue = 1;

        ArgumentNullException.ThrowIfNull(context);
        context.Selection.Validate();
        if (context.Selection.Target != Target)
        {
            throw new InvalidOperationException(InvalidSelection);
        }
        var scripts = IsolatedPostgresBootstrap.FindScripts(context);
        var password = context.Builder.AddParameter(PasswordName,
            Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(SecretBytes)), secret: true);
        var primaryName = NodePrefix + Step;
        var primary = context.Builder.AddPostgres(primaryName, password: password)
            .WithImage(Image).WithImageTag(Tag).WithImageSHA256(BenchmarkResources.PostgresDigest[DigestPrefixLength..])
            .WithContainerNetworkAlias(primaryName);
        IsolatedPostgresBootstrap.Configure(primary, context.DataDirectory(primaryName), scripts, password, null,
            context.Selection.NodeCount - Step, KeyLoad.AppHost.Hosting.AppHostOptionsRegistration.Get(context.Builder).Deployment);
        context.BindSetting(Connection, primary.Resource.ConnectionStringExpression);
        context.BindEndpoint(IndexValue, primary, Tcp);
        for (var index = IndexInitialValue; index < context.Selection.NodeCount; index++)
        {
            var name = NodePrefix + (index + Step).ToString(CultureInfo.InvariantCulture);
            var standby = context.Builder.AddContainer(name, Image, Tag).WithImageSHA256(BenchmarkResources.PostgresDigest[DigestPrefixLength..])
                .WithContainerNetworkAlias(name).WithEndpoint(targetPort: Port, name: Tcp, scheme: Tcp).WaitFor(primary);
            IsolatedPostgresBootstrap.Configure(standby, context.DataDirectory(name), scripts, password,
                index.ToString(CultureInfo.InvariantCulture), context.Selection.NodeCount - Step, KeyLoad.AppHost.Hosting.AppHostOptionsRegistration.Get(context.Builder).Deployment);
            context.BindEndpoint(index, standby, Tcp);
        }
        context.BindImage(Registry + Image + TagSeparator + Tag + DigestSeparator + BenchmarkResources.PostgresDigest);
    }
}
