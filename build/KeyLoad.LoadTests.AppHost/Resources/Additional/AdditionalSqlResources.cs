using System.Security.Cryptography;
using KeyLoad.Comparisons;

namespace KeyLoad.LoadTests.AppHost.BenchmarkComparisons;

internal static class AdditionalSqlResources
{
    private const string TimescaleName = "isolated-timescale", SqlServerName = "isolated-sqlserver", MySqlName = "isolated-mysql";
    private const string PasswordName = "additional-database-password", PasswordPrefix = "Kl1!";
    private const string Tcp = "tcp", Docker = "docker.io/", Separator = ":", DigestSeparator = "@";
    private const string Invalid = "AdditionalSqlResourceSelectionRejected";
    private const string PgHost = "Host=", PgPort = ";Port=", PgUser = ";Username=", PgSecret = ";Password=", PgDatabase = ";Database=";
    private const string SqlHost = "Server=", SqlPort = ",", SqlUser = ";User ID=sa;Password=", SqlEncryption = ";Encrypt=True;TrustServerCertificate=True";
    private const string MySqlHost = "Server=", MySqlPortSetting = ";Port=", MySqlUser = ";User ID=root;Password=", MySqlTls = ";SslMode=Preferred";
    private const int SecretBytes = 32, SingleNode = 1, EndpointIndex = 0;
    private const int PostgresPort = 5432, SqlServerPort = 1433, MySqlPort = 3306;

    internal static void Add(IsolatedResourceContext context)
    {
        if (context.Selection.NodeCount != SingleNode)
        {
            throw new InvalidOperationException(Invalid);
        }
        var password = context.Builder.AddParameter(PasswordName,
            PasswordPrefix + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(SecretBytes)), secret: true);
        switch (context.Selection.Target)
        {
            case BenchmarkAdditionalTargets.Timescale:
                AddTimescale(context, password);
                break;
            case BenchmarkAdditionalTargets.SqlServer:
                AddSqlServer(context, password);
                break;
            case BenchmarkAdditionalTargets.MySql:
                AddMySql(context, password);
                break;
            default:
                throw new InvalidOperationException(Invalid);
        }
    }

    private static void AddTimescale(IsolatedResourceContext context, IResourceBuilder<ParameterResource> password)
    {
        const string User = "POSTGRES_USER", Database = "POSTGRES_DB", Secret = "POSTGRES_PASSWORD", Postgres = "postgres";
        const string Mount = "/var/lib/postgresql", Data = "PGDATA", DataPath = "/var/lib/postgresql/18/docker";
        const string Shell = "postgres", Setting = "-c", Fsync = "fsync=on", Commit = "synchronous_commit=on";
        var node = context.Builder.AddContainer(TimescaleName, BenchmarkAdditionalImages.TimescaleImage, BenchmarkAdditionalImages.TimescaleTag)
            .WithImageSHA256(BenchmarkAdditionalImages.TimescaleDigest[BenchmarkAdditionalImages.DigestPrefixLength..])
            .WithEnvironment(User, Postgres).WithEnvironment(Database, Postgres).WithEnvironment(Secret, password)
            .WithEnvironment(Data, DataPath).WithVolume(target: Mount)
            .WithArgs(Shell, Setting, Fsync, Setting, Commit)
            .WithEndpoint(targetPort: PostgresPort, name: Tcp, scheme: Tcp);
        var endpoint = node.GetEndpoint(Tcp);
        AdditionalSqlReadiness.Bind(context, node, ReferenceExpression.Create(
            $"{PgHost}{endpoint.Property(EndpointProperty.Host)}{PgPort}{endpoint.Property(EndpointProperty.Port)}{PgUser}{Postgres}{PgSecret}{password}{PgDatabase}{Postgres}"));
        context.BindEndpoint(EndpointIndex, node, Tcp);
        context.BindImage(Docker + BenchmarkAdditionalImages.TimescaleImage + Separator + BenchmarkAdditionalImages.TimescaleTag + DigestSeparator + BenchmarkAdditionalImages.TimescaleDigest);
    }

    private static void AddSqlServer(IsolatedResourceContext context, IResourceBuilder<ParameterResource> password)
    {
        const string Accept = "ACCEPT_EULA", Yes = "Y", Edition = "MSSQL_PID", Developer = "Developer", Secret = "MSSQL_SA_PASSWORD";
        const string Mount = "/var/opt/mssql";
        var node = context.Builder.AddContainer(SqlServerName, BenchmarkAdditionalImages.SqlServerImage, BenchmarkAdditionalImages.SqlServerTag)
            .WithImageSHA256(BenchmarkAdditionalImages.SqlServerDigest[BenchmarkAdditionalImages.DigestPrefixLength..])
            .WithEnvironment(Accept, Yes).WithEnvironment(Edition, Developer).WithEnvironment(Secret, password)
            .WithVolume(target: Mount).WithEndpoint(targetPort: SqlServerPort, name: Tcp, scheme: Tcp);
        var endpoint = node.GetEndpoint(Tcp);
        AdditionalSqlReadiness.Bind(context, node, ReferenceExpression.Create(
            $"{SqlHost}{endpoint.Property(EndpointProperty.Host)}{SqlPort}{endpoint.Property(EndpointProperty.Port)}{SqlUser}{password}{SqlEncryption}"));
        context.BindEndpoint(EndpointIndex, node, Tcp);
        context.BindImage(BenchmarkAdditionalImages.SqlServerImage + Separator + BenchmarkAdditionalImages.SqlServerTag + DigestSeparator + BenchmarkAdditionalImages.SqlServerDigest);
    }

    private static void AddMySql(IsolatedResourceContext context, IResourceBuilder<ParameterResource> password)
    {
        const string Secret = "MYSQL_ROOT_PASSWORD", Host = "MYSQL_ROOT_HOST", AnyHost = "%", Mount = "/var/lib/mysql";
        const string Flush = "--innodb-flush-log-at-trx-commit=1", Sync = "--sync-binlog=1";
        var node = context.Builder.AddContainer(MySqlName, BenchmarkAdditionalImages.MySqlImage, BenchmarkAdditionalImages.MySqlTag)
            .WithImageSHA256(BenchmarkAdditionalImages.MySqlDigest[BenchmarkAdditionalImages.DigestPrefixLength..])
            .WithEnvironment(Secret, password).WithEnvironment(Host, AnyHost).WithVolume(target: Mount)
            .WithArgs(Flush, Sync).WithEndpoint(targetPort: MySqlPort, name: Tcp, scheme: Tcp);
        var endpoint = node.GetEndpoint(Tcp);
        AdditionalSqlReadiness.Bind(context, node, ReferenceExpression.Create(
            $"{MySqlHost}{endpoint.Property(EndpointProperty.Host)}{MySqlPortSetting}{endpoint.Property(EndpointProperty.Port)}{MySqlUser}{password}{MySqlTls}"));
        context.BindEndpoint(EndpointIndex, node, Tcp);
        context.BindImage(Docker + BenchmarkAdditionalImages.MySqlImage + Separator + BenchmarkAdditionalImages.MySqlTag + DigestSeparator + BenchmarkAdditionalImages.MySqlDigest);
    }
}
