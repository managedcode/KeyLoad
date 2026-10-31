using System.Globalization;
using System.Security.Cryptography;

namespace KeyLoad.LoadTests.AppHost.BenchmarkComparisons;

internal static class AdditionalHttpResources
{
    private const string ClickHouse = "isolated-clickhouse", Influx = "isolated-influxdb", Http = "http";
    private const string Docker = "docker.io/", Separator = ":", DigestSeparator = "@";
    private const string Invalid = "AdditionalHttpResourceSelectionRejected";
    private const int SingleNode = 1, EndpointIndex = 0, ClickHousePort = 8123, InfluxPort = 8181, SecretBytes = 32;

    internal static void Add(IsolatedResourceContext context)
    {
        if (context.Selection.NodeCount != SingleNode)
        {
            throw new InvalidOperationException(Invalid);
        }
        if (context.Selection.Target == BenchmarkDatabases.ClickHouse)
        {
            AddClickHouse(context);
        }
        else if (context.Selection.Target == BenchmarkDatabases.InfluxDb)
        {
            AddInflux(context);
        }
        else
        {
            throw new InvalidOperationException(Invalid);
        }
    }

    private static void AddClickHouse(IsolatedResourceContext context)
    {
        const string Secret = "clickhouse-password", UserEnvironment = "CLICKHOUSE_USER", User = "default";
        const string PasswordEnvironment = "CLICKHOUSE_PASSWORD", Mount = "/var/lib/clickhouse", Ready = "/ping";
        const string UserSetting = "User", PasswordSetting = "Password";
        var password = context.Builder.AddParameter(Secret, Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(SecretBytes)), secret: true);
        var node = context.Builder.AddContainer(ClickHouse, BenchmarkAdditionalImages.ClickHouseImage, BenchmarkAdditionalImages.ClickHouseTag)
            .WithImageSHA256(BenchmarkAdditionalImages.ClickHouseDigest[BenchmarkAdditionalImages.DigestPrefixLength..])
            .WithEnvironment(UserEnvironment, User).WithEnvironment(PasswordEnvironment, password)
            .WithVolume(target: Mount).WithHttpEndpoint(targetPort: ClickHousePort, name: Http).WithHttpHealthCheck(Ready);
        context.BindEndpoint(EndpointIndex, node, Http);
        context.BindSetting(UserSetting, User);
        context.BindSetting(PasswordSetting, password);
        context.BindImage(Docker + BenchmarkAdditionalImages.ClickHouseImage + Separator + BenchmarkAdditionalImages.ClickHouseTag + DigestSeparator + BenchmarkAdditionalImages.ClickHouseDigest);
    }

    private static void AddInflux(IsolatedResourceContext context)
    {
        const string Mount = "/var/lib/influxdb3", Ready = "/health";
        const string Serve = "serve", NodeId = "--node-id=benchmark", Store = "--object-store=file";
        const string Directory = "--data-dir=" + Mount, Bind = "--http-bind=0.0.0.0:8181", Auth = "--without-auth";
        const string QueryMemory = "--exec-mem-pool-size=", Percent = "%";
        const string SnapshotFiles = "--wal-files-per-snapshot=";
        var deployment = context.Deployment.Value;
        var queryMemory = QueryMemory + deployment.InfluxQueryMemoryPercent.ToString(CultureInfo.InvariantCulture) + Percent;
        var snapshotFiles = SnapshotFiles + deployment.InfluxWalFilesPerSnapshot.ToString(CultureInfo.InvariantCulture);
        var node = context.Builder.AddContainer(Influx, BenchmarkAdditionalImages.InfluxImage, BenchmarkAdditionalImages.InfluxTag)
            .WithImageSHA256(BenchmarkAdditionalImages.InfluxDigest[BenchmarkAdditionalImages.DigestPrefixLength..])
            .WithArgs(Serve, NodeId, Store, Directory, Bind, Auth, queryMemory, snapshotFiles).WithVolume(target: Mount)
            .WithHttpEndpoint(targetPort: InfluxPort, name: Http).WithHttpHealthCheck(Ready);
        context.BindEndpoint(EndpointIndex, node, Http);
        context.BindImage(Docker + BenchmarkAdditionalImages.InfluxImage + Separator + BenchmarkAdditionalImages.InfluxTag + DigestSeparator + BenchmarkAdditionalImages.InfluxDigest);
    }
}
